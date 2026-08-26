using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace TNovTasks.TasksPro
{
    /// <summary>
    /// Геометрия группы-задания в self-contained .glb для просмотра на сайте.
    /// Смысл — чтобы КР увидел ТЕ САМЫЕ отверстия, которые ему выдали, не запуская
    /// Revit: сами отверстия (роль target, красные) и ближайшее окружение — стены,
    /// перекрытия, инженерия рядом (роль neighbor, серое полупрозрачное).
    ///
    /// Координаты: Revit (футы, Z-up) → glTF (метры, Y-up), с рецентрированием в
    /// центр группы, иначе мировые координаты Revit убивают точность float.
    /// glTF собирается вручную, без сторонних библиотек: тащить в процесс Revit
    /// лишние сборки — верный способ поймать конфликт версий.
    ///
    /// Перенесено из плагина «Вопросы» (TNovPro.Issues/Revit/GeometryExporter.cs),
    /// где этот код уже возит 3D в TNovPRO; по ТЗ ссылку на чужой проект не
    /// добавляем, поэтому нужное лежит здесь.
    /// </summary>
    public static class ProGeometry
    {
        private const double FeetToMeters = 0.3048;
        private const double PadFeet = 8.0;      // ~2,4 м — зона сбора соседей вокруг группы
        private const int MaxNeighbors = 150;    // ближайшие N: иначе фрагмент разрастётся на весь этаж

        // Отверстия смотрят в упор — среднее качество; окружение грубое, чтобы
        // полторы сотни элементов уложились в серверный лимит .glb (40 МБ).
        private const ViewDetailLevel TargetDetail = ViewDetailLevel.Medium;
        private const ViewDetailLevel NeighborDetail = ViewDetailLevel.Coarse;

        public sealed class Result
        {
            public byte[] Glb;
            public int TriangleCount;
            public int TargetCount;
            public int NeighborCount;
            public bool Truncated;      // соседей было больше MaxNeighbors
            public string Error;        // не собрали — текст для лога
        }

        /// <summary>
        /// Собрать фрагмент по элементам группы. Ошибку не бросаем: 3D — приятное
        /// дополнение к заданию, из-за него выдача падать не должна.
        /// </summary>
        public static Result Export(Document doc, ICollection<ElementId> targetIds)
        {
            var result = new Result();
            try
            {
                if (doc == null || targetIds == null || targetIds.Count == 0)
                {
                    result.Error = "не переданы элементы группы";
                    return result;
                }

                var targets = new List<Element>();
                var targetSet = new HashSet<int>();
                foreach (var id in targetIds)
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;
                    targets.Add(el);
                    targetSet.Add(RevitApiCompat.ElementIdIntValue(id));
                }
                if (targets.Count == 0) { result.Error = "элементы группы не найдены в модели"; return result; }

                var union = UnionBox(targets);
                if (union == null) { result.Error = "у элементов группы нет габаритов"; return result; }
                var center = (union.Min + union.Max) * 0.5;

                // Кандидаты в соседи: модельные элементы в небольшой зоне вокруг
                // группы, от ближайшего к дальнему.
                var min = new XYZ(union.Min.X - PadFeet, union.Min.Y - PadFeet, union.Min.Z - PadFeet);
                var max = new XYZ(union.Max.X + PadFeet, union.Max.Y + PadFeet, union.Max.Z + PadFeet);
                var filter = new BoundingBoxIntersectsFilter(new Outline(min, max));

                var candidates = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(filter)
                    .ToElements()
                    .Where(el => !targetSet.Contains(RevitApiCompat.ElementIdIntValue(el.Id)))
                    .Where(el => el.Category != null && el.Category.CategoryType == CategoryType.Model)
                    .Select(el => new { el, dist = DistanceToCenter(el, center) })
                    .OrderBy(x => x.dist)
                    .Select(x => x.el)
                    .ToList();

                // Каждый элемент — отдельный меш со своим ElementId: во вьювере
                // выбирается любой, а не только целевой.
                var meshList = new List<RoleMesh>();
                var properties = new Dictionary<string, object>();
                var voidMaterials = VoidMaterials(doc);

                foreach (var el in targets)
                {
                    long id = RevitApiCompat.ElementIdIntValue(el.Id);
                    var rm = new RoleMesh("target", id);
                    if (AddElement(el, rm, center, TargetDetail, voidMaterials))
                    {
                        meshList.Add(rm);
                        AddPassport(properties, el, id);
                    }
                }

                int neighbors = 0;
                foreach (var el in candidates)
                {
                    if (neighbors >= MaxNeighbors) break;
                    long id = RevitApiCompat.ElementIdIntValue(el.Id);
                    var rm = new RoleMesh("neighbor", id);
                    if (AddElement(el, rm, center, NeighborDetail, voidMaterials))
                    {
                        meshList.Add(rm);
                        AddPassport(properties, el, id);
                        neighbors++;
                    }
                }

                var nonEmpty = meshList.Where(m => m.Positions.Count > 0).ToList();
                if (nonEmpty.Count == 0) { result.Error = "не удалось триангулировать группу"; return result; }

                result.Glb = BuildGlb(nonEmpty, properties);
                result.TriangleCount = nonEmpty.Sum(m => m.Positions.Count / 9); // 9 float = 3 вершины = 1 треугольник
                result.TargetCount = nonEmpty.Count(m => m.Role == "target");
                result.NeighborCount = neighbors;
                result.Truncated = neighbors >= MaxNeighbors && candidates.Count > neighbors;
                return result;
            }
            catch (Exception ex)
            {
                result.Glb = null;
                result.Error = ex.Message;
                return result;
            }
        }

        /// <summary>Короткий паспорт элемента — что это такое, если во вьювере по нему щёлкнут.</summary>
        private static void AddPassport(Dictionary<string, object> into, Element el, long id)
        {
            var key = id.ToString();
            if (into.ContainsKey(key)) return;
            try
            {
                var map = new Dictionary<string, object>
                {
                    ["category"] = el.Category != null ? el.Category.Name : null,
                    ["name"] = el.Name,
                };
                var mark = el.LookupParameter("Марка");
                if (mark != null && mark.StorageType == StorageType.String)
                {
                    var v = mark.AsString();
                    if (!string.IsNullOrEmpty(v)) map["mark"] = v;
                }
                into[key] = map;
            }
            catch { /* без паспорта элемент всё равно показывается */ }
        }

        private static double DistanceToCenter(Element el, XYZ center)
        {
            var bb = el.get_BoundingBox(null);
            if (bb == null) return double.MaxValue;
            var c = (bb.Min + bb.Max) * 0.5;
            return c.DistanceTo(center);
        }

        private static BoundingBoxXYZ UnionBox(IEnumerable<Element> elems)
        {
            double minx = double.MaxValue, miny = double.MaxValue, minz = double.MaxValue;
            double maxx = double.MinValue, maxy = double.MinValue, maxz = double.MinValue;
            bool any = false;
            foreach (var el in elems)
            {
                var bb = el.get_BoundingBox(null);
                if (bb == null) continue;
                any = true;
                minx = Math.Min(minx, bb.Min.X); miny = Math.Min(miny, bb.Min.Y); minz = Math.Min(minz, bb.Min.Z);
                maxx = Math.Max(maxx, bb.Max.X); maxy = Math.Max(maxy, bb.Max.Y); maxz = Math.Max(maxz, bb.Max.Z);
            }
            if (!any) return null;
            return new BoundingBoxXYZ { Min = new XYZ(minx, miny, minz), Max = new XYZ(maxx, maxy, maxz) };
        }

        /// <summary>
        /// Материалы-«пустоты»: ими в семействах залиты технические тела (зона
        /// открывания двери — «N Воздух»). В модели их не видно, а в выгрузке это
        /// обычный ящик поверх элемента.
        /// </summary>
        private static HashSet<ElementId> VoidMaterials(Document doc)
        {
            var set = new HashSet<ElementId>();
            if (doc == null) return set;
            try
            {
                foreach (var m in new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>())
                {
                    var name = m.Name ?? string.Empty;
                    if (name.IndexOf("воздух", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("air", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        set.Add(m.Id);
                    }
                }
            }
            catch { /* нет доступа к материалам — ничего не отсекаем */ }
            return set;
        }

        private static bool AddElement(Element el, RoleMesh mesh, XYZ center, ViewDetailLevel detail,
                                       HashSet<ElementId> voidMaterials)
        {
            if (el == null) return false;
            var opt = new Options { ComputeReferences = false, DetailLevel = detail };
            GeometryElement ge;
            try { ge = el.get_Geometry(opt); }
            catch { return false; }
            if (ge == null) return false;

            int before = mesh.Positions.Count;
            CollectTriangles(ge, voidMaterials, (a, b, c) =>
            {
                var ga = ToGltf(a, center);
                var gb = ToGltf(b, center);
                var gc = ToGltf(c, center);
                var n = Normalize(Cross(Sub(gb, ga), Sub(gc, ga)));  // плоская нормаль
                AddVertex(mesh, ga, n);
                AddVertex(mesh, gb, n);
                AddVertex(mesh, gc, n);
            });
            return mesh.Positions.Count > before;
        }

        private static void CollectTriangles(GeometryElement ge, HashSet<ElementId> voidMaterials,
                                             Action<XYZ, XYZ, XYZ> emit)
        {
            bool IsVoid(ElementId materialId) =>
                voidMaterials != null && materialId != null && voidMaterials.Contains(materialId);

            foreach (var obj in ge)
            {
                var solid = obj as Solid;
                if (solid != null && solid.Faces.Size > 0)
                {
                    foreach (Face f in solid.Faces)
                    {
                        if (IsVoid(f.MaterialElementId)) continue;
                        EmitMesh(f.Triangulate(), emit);
                    }
                    continue;
                }
                var m = obj as Mesh;
                if (m != null)
                {
                    if (!IsVoid(m.MaterialElementId)) EmitMesh(m, emit);
                    continue;
                }
                var gi = obj as GeometryInstance;
                if (gi != null)
                {
                    // GetInstanceGeometry() отдаёт геометрию уже в координатах модели.
                    var inst = gi.GetInstanceGeometry();
                    if (inst != null) CollectTriangles(inst, voidMaterials, emit);
                }
            }
        }

        private static void EmitMesh(Mesh m, Action<XYZ, XYZ, XYZ> emit)
        {
            if (m == null) return;
            int n = m.NumTriangles;
            for (int i = 0; i < n; i++)
            {
                var t = m.get_Triangle(i);
                emit(t.get_Vertex(0), t.get_Vertex(1), t.get_Vertex(2));
            }
        }

        // Revit (футы, Z-up) → glTF (метры, Y-up) с рецентрированием.
        private static double[] ToGltf(XYZ p, XYZ c) => new[]
        {
            (p.X - c.X) * FeetToMeters,
            (p.Z - c.Z) * FeetToMeters,
            -(p.Y - c.Y) * FeetToMeters,
        };

        private static void AddVertex(RoleMesh mesh, double[] pos, double[] nrm)
        {
            mesh.Positions.Add((float)pos[0]); mesh.Positions.Add((float)pos[1]); mesh.Positions.Add((float)pos[2]);
            mesh.Normals.Add((float)nrm[0]); mesh.Normals.Add((float)nrm[1]); mesh.Normals.Add((float)nrm[2]);
        }

        private static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };

        private static double[] Cross(double[] a, double[] b) => new[]
        {
            a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0],
        };

        private static double[] Normalize(double[] v)
        {
            double len = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            if (len < 1e-12) return new double[] { 0, 1, 0 };
            return new[] { v[0] / len, v[1] / len, v[2] / len };
        }

        private sealed class RoleMesh
        {
            public string Role { get; }
            public long ElementId { get; }
            public List<float> Positions { get; } = new List<float>();
            public List<float> Normals { get; } = new List<float>();
            public RoleMesh(string role, long elementId) { Role = role; ElementId = elementId; }

            /// <summary>Имя узла в glTF: вьювер находит элемент по подстроке «Element_&lt;id&gt;».</summary>
            public string GltfName => "Element_" + ElementId;
        }

        private static int RoleToMaterial(string role) => role == "target" ? 0 : 1;

        private static object[] MaterialsJson() => new object[]
        {
            new { name = "target",   pbrMetallicRoughness = new { baseColorFactor = new[] { 0.94, 0.27, 0.27, 1.0 },  metallicFactor = 0.1, roughnessFactor = 0.7 }, doubleSided = true },
            new { name = "neighbor", pbrMetallicRoughness = new { baseColorFactor = new[] { 0.45, 0.48, 0.53, 0.45 }, metallicFactor = 0.1, roughnessFactor = 0.8 }, alphaMode = "BLEND", doubleSided = true },
        };

        private static byte[] BuildGlb(List<RoleMesh> meshes, Dictionary<string, object> properties)
        {
            var bin = new MemoryStream();
            var bw = new BinaryWriter(bin);

            void AlignBin()
            {
                while (bin.Length % 4 != 0) bw.Write((byte)0);
            }

            var bufferViews = new List<object>();
            var accessors = new List<object>();
            var gltfMeshes = new List<object>();
            var nodes = new List<object>();
            var nodeIdx = new List<int>();

            foreach (var mesh in meshes)
            {
                int vcount = mesh.Positions.Count / 3;

                AlignBin();
                int posOffset = (int)bin.Length;
                ComputeMinMax(mesh.Positions, out var pmin, out var pmax);
                foreach (var f in mesh.Positions) bw.Write(f);
                int posLen = (int)bin.Length - posOffset;
                int posView = bufferViews.Count;
                bufferViews.Add(new { buffer = 0, byteOffset = posOffset, byteLength = posLen, target = 34962 });
                int posAcc = accessors.Count;
                accessors.Add(new { bufferView = posView, componentType = 5126, count = vcount, type = "VEC3", min = pmin, max = pmax });

                AlignBin();
                int nrmOffset = (int)bin.Length;
                foreach (var f in mesh.Normals) bw.Write(f);
                int nrmLen = (int)bin.Length - nrmOffset;
                int nrmView = bufferViews.Count;
                bufferViews.Add(new { buffer = 0, byteOffset = nrmOffset, byteLength = nrmLen, target = 34962 });
                int nrmAcc = accessors.Count;
                accessors.Add(new { bufferView = nrmView, componentType = 5126, count = vcount, type = "VEC3" });

                int meshIdx = gltfMeshes.Count;
                gltfMeshes.Add(new
                {
                    name = mesh.GltfName,
                    primitives = new[]
                    {
                        new
                        {
                            attributes = new { POSITION = posAcc, NORMAL = nrmAcc },
                            material = RoleToMaterial(mesh.Role),
                        },
                    },
                });

                var node = new Dictionary<string, object>
                {
                    ["mesh"] = meshIdx,
                    ["name"] = mesh.GltfName,
                    ["extras"] = new Dictionary<string, object>
                    {
                        ["revitElementId"] = mesh.ElementId,
                        ["role"] = mesh.Role,
                    },
                };
                nodes.Add(node);
                nodeIdx.Add(nodes.Count - 1);
            }

            bw.Flush();
            byte[] binBytes = bin.ToArray();

            // Паспорта едут вместе с геометрией в корневых extras: вьюверу не нужен
            // отдельный запрос и серверная схема под них.
            var rootExtras = new Dictionary<string, object>
            {
                ["tnovpro"] = new Dictionary<string, object>
                {
                    ["schema"] = 1,
                    ["units"] = "m",
                    ["source"] = "TNovTasks",
                    ["elements"] = properties ?? new Dictionary<string, object>(),
                },
            };

            var gltf = new
            {
                asset = new { version = "2.0", generator = "TNovTasks GLB exporter" },
                scene = 0,
                scenes = new[] { new { nodes = nodeIdx.ToArray() } },
                nodes,
                meshes = gltfMeshes,
                materials = MaterialsJson(),
                accessors,
                bufferViews,
                buffers = new[] { new { byteLength = binBytes.Length } },
                extras = rootExtras,
            };

            string json = JsonConvert.SerializeObject(gltf);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

            // Паддинг чанков до кратности 4: JSON — пробелами, BIN — нулями.
            byte[] jsonPadded = PadTo4(jsonBytes, 0x20);
            byte[] binPadded = PadTo4(binBytes, 0x00);

            int total = 12 + 8 + jsonPadded.Length + 8 + binPadded.Length;

            var outMs = new MemoryStream();
            var ow = new BinaryWriter(outMs);
            ow.Write(0x46546C67);            // "glTF"
            ow.Write(2);                     // version
            ow.Write(total);
            ow.Write(jsonPadded.Length);
            ow.Write(0x4E4F534A);            // "JSON"
            ow.Write(jsonPadded);
            ow.Write(binPadded.Length);
            ow.Write(0x004E4942);            // "BIN\0"
            ow.Write(binPadded);
            ow.Flush();
            return outMs.ToArray();
        }

        private static byte[] PadTo4(byte[] data, byte pad)
        {
            int rem = data.Length % 4;
            if (rem == 0) return data;
            var padded = new byte[data.Length + (4 - rem)];
            Array.Copy(data, padded, data.Length);
            for (int i = data.Length; i < padded.Length; i++) padded[i] = pad;
            return padded;
        }

        private static void ComputeMinMax(List<float> pos, out float[] min, out float[] max)
        {
            min = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
            max = new[] { float.MinValue, float.MinValue, float.MinValue };
            for (int i = 0; i < pos.Count; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    float v = pos[i + k];
                    if (v < min[k]) min[k] = v;
                    if (v > max[k]) max[k] = v;
                }
            }
        }
    }
}
