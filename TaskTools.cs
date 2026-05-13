using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using TNovCommon;

namespace TNovTasks
{
    public class TaskTools
    {
        public static List<HoleGroup> GetHoleGroups(in Document linkDoc, in Document doc, in string userName)
        {
            //подкласс 1
            string groups1 = TaskTools.GetGroupNames(linkDoc, doc);

            int index = groups1.LastIndexOf('|');
            groups1 = groups1.Remove(index);
            string[] groups = groups1.Split('|');

            List<HoleGroup> holeGroups = new List<HoleGroup>();

            string docName = linkDoc.Title.ToString();
            docName = docName.Replace(",", " "); string docNameUserName = "_" + userName; docName = docName.Replace(docNameUserName, "");
            // Десериализация данных из базы
            List<HoleGroupBaseItem> existingItems = new List<HoleGroupBaseItem>();
            string jsonFilePath = nova.novaserver + "_TNov/tasks/" + docName + ".json";
            if (File.Exists(jsonFilePath))
            {
                string jsonContent = File.ReadAllText(jsonFilePath);
                existingItems = JsonConvert.DeserializeObject<List<HoleGroupBaseItem>>(jsonContent)
                                ?? new List<HoleGroupBaseItem>();
            }


            foreach (string group in groups)
            {
                string[] nameParts = group.Split('=');
                string[] shortNameParts = nameParts[0].Split('_');
                string pt2 = ""; if (shortNameParts.Length > 1) pt2 = shortNameParts[1];
                string pt3 = ""; if (shortNameParts.Length > 2) pt3 = shortNameParts[2];
                int order = 0;
                bool buttonVisibility = true; string buttonText = "Детальный анализ";
                string buttonToolTip = "Просмотреть информацию о каждом отверстии в группе";
                string status = nameParts[1];
                if (status.Contains("не вставлялось"))
                {
                    buttonText = "Вставить"; order = 2;
                    buttonToolTip = "Первичная вставка группы с отверстиями в текущую модель";
                }
                if (status.Contains("Марка") || status.Contains("КР"))
                {
                    buttonVisibility = false; order = 1;
                }

                if (status.Contains("Актуально")) order = 3;

                string version = ""; string dateinitiator = ""; 
                // Ищем запись с таким именем группы
                HoleGroupBaseItem existingItem = existingItems.FirstOrDefault(item => item.HoleGroupName == nameParts[0]);
                if (existingItem != null)
                {
                    //считываем данные из базы
                    version = existingItem.TaskVersion; 
                    dateinitiator = existingItem.TaskDate + " " + existingItem.Initiator;

                    if (status.Contains("не вставлялось")) { }
                    else
                    {
                        string docName1 = doc.Title.ToString();
                        docName1 = docName1.Replace(",", " "); docName1 = docName1.Replace(docNameUserName, "");
                        if (docName1.Contains("отсоед")) { }
                        else 
                        {
                            //обновляем атрибуты в базе
                            existingItem.STModelName = docName1;
                            switch (order)
                            {
                                case 0:
                                    existingItem.STStatus = "Неактуально";
                                    existingItem.STMisc = "См. детальный анализ в модели КР";
                                    break;
                                case 1:
                                    existingItem.STStatus = "Неактуально";
                                    existingItem.STMisc = "В модели Заданий не заполнены марки или не согласованы элементы задания";
                                    break;
                                case 3:
                                    existingItem.STStatus = "Актуально"; existingItem.STMisc = ""; break;
                            }
                            string currentDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                            existingItem.STCheckDate = currentDateTime;
                        }
                        
                    }
                }

                holeGroups.Add(new HoleGroup
                {
                    HoleGroupName = group,
                    HoleGroupNamePart1 = shortNameParts[0],
                    HoleGroupNamePart2 = pt2,
                    HoleGroupNamePart3 = pt3,
                    HoleGroupSet = nameParts[2],
                    HoleGroupStatus = nameParts[1],
                    ButtonText = buttonText,
                    ButtonToolTip = buttonToolTip,
                    IsButtonVisible = buttonVisibility,
                    Order = order,
                    HoleGroupVersion = version,
                    HoleGroupDateInitiator = dateinitiator
                });
            }
            string updatedJson = JsonConvert.SerializeObject(existingItems, Formatting.Indented);
            try
            {
                File.WriteAllText(jsonFilePath, updatedJson);
            }
            catch (Exception ex)
            {
                //new InfoWindow280($"Ошибка записи в базу: {ex.Message}. Попробуйте, пожалуйста, повторить.").ShowDialog();
            }
            holeGroups = holeGroups.OrderBy(h => h.Order).ToList();
            return holeGroups;
        }
        public static string GetGroupNames(in Document linkDoc, in Document doc)
        {
            //параметры
            BuiltInParameter mrk = BuiltInParameter.ALL_MODEL_MARK; //Марка
            ElementId familyNameParamId = new ElementId(-1002002); //id параметра Имя семейства
            Guid adskGparamGuid = new Guid("3de5f1a4-d560-4fa8-a74f-25d250fb3401");//ADSK_Группирование
            Guid NTaskApprovedBIMParamGuid = new Guid("94587b6e-5bdd-4fe8-bea4-4996c32801c4");//N_Согласовано BIM
            Guid NTaskApprovedSTParamGuid = new Guid("7cb33aa5-8106-4e4c-8038-6691e34f438c");//N_Согласовано КР
            //04.2026: параметр N_Согласовано рук исключен как устаревший, для совместимости заполняется как "1"

            List<Group> linkGroups = new FilteredElementCollector(linkDoc).OfCategory(BuiltInCategory.OST_IOSModelGroups)
                .WhereElementIsNotElementType()
                .Cast<Group>()
                .ToList();

            List<Group> groups = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_IOSModelGroups)
                .WhereElementIsNotElementType()
                .Cast<Group>()
                .ToList();

            List<string> groupTxtList = new List<string>();
            //проходим по группам в связанной модели
            foreach (var linkGroup in linkGroups)
            {
                Logger.Log(linkGroup.Name, 1);
                string[] nameParts = linkGroup.Name.Split('_');
                string shortName = linkGroup.Name;
                if (nameParts.Length > 2) shortName = nameParts[0] + '_' + nameParts[1] + '_' + nameParts[2]; //учет групп, созданных по старой концепции
                string status = "";
                //проверяем элементы в задании на заполненность Марки
                ElementFilter elementFilter = (ElementFilter)new ElementParameterFilter(ParameterFilterRuleFactory.CreateContainsRule(familyNameParamId, "pmN.Отверстие", true));
                IList<ElementId> linkGroupElems = linkGroup.GetDependentElements(elementFilter);
                ElementFilter elementFilter21 = (ElementFilter)new ElementParameterFilter(ParameterFilterRuleFactory.CreateContainsRule(familyNameParamId, "pmN.Рама под оборудование", true));
                ElementFilter elementFilter22 = (ElementFilter)new ElementParameterFilter(ParameterFilterRuleFactory.CreateContainsRule(familyNameParamId, "pmN.Задание на шахту", true));
                ElementFilter elementFilter23 = (ElementFilter)new ElementParameterFilter(ParameterFilterRuleFactory.CreateContainsRule(familyNameParamId, "pmN.Задание на приямок", true));
                List<ElementId> linkGroupElems21 = linkGroup.GetDependentElements(elementFilter21).ToList();
                List<ElementId> linkGroupElems22 = linkGroup.GetDependentElements(elementFilter22).ToList();
                List<ElementId> linkGroupElems23 = linkGroup.GetDependentElements(elementFilter23).ToList();
                List<ElementId> linkGroupElems2 = linkGroupElems21.Union(linkGroupElems22).Union(linkGroupElems23).ToList();
                string badElementIds = "";
                int k = 0;
                //отверстия
                foreach (var linkGroupElem in linkGroupElems)
                {
                    Element elem = linkDoc.GetElement(linkGroupElem);
                    string mrkvalue = elem.get_Parameter(mrk).AsValueString();
                    if (mrkvalue == null || mrkvalue == "") badElementIds += linkGroupElem.ToString() + " ";
                    int linkElem_coordStatusST_int = elem.get_Parameter(NTaskApprovedSTParamGuid).AsInteger();
                    if (linkElem_coordStatusST_int != 1) k++;
                    int linkElem_coordStatusBIM_int = elem.get_Parameter(NTaskApprovedBIMParamGuid).AsInteger();
                    if (linkElem_coordStatusBIM_int != 1) k++;
                }
                //прочие задания
                foreach (var linkGroupElem in linkGroupElems2)
                {
                    Element elem = linkDoc.GetElement(linkGroupElem);
                    string mrkvalue = elem.get_Parameter(mrk).AsValueString();
                    if (mrkvalue == null || mrkvalue == "") badElementIds += linkGroupElem.ToString() + " ";
                    int linkElem_coordStatusST_int = elem.get_Parameter(NTaskApprovedSTParamGuid).AsInteger();
                    if (linkElem_coordStatusST_int != 1) k++;
                    int linkElem_coordStatusBIM_int = elem.get_Parameter(NTaskApprovedBIMParamGuid).AsInteger();
                    if (linkElem_coordStatusBIM_int != 1) k++;
                }
                if (badElementIds.Length > 0) Logger.Log("Элементы с незаполненной Маркой: " + badElementIds, 1);

                //adsk группирование
                string gSet = "Не заполнено";
                bool setExist = Param.ParamExistByGuid(adskGparamGuid, linkGroup);
                if (setExist)
                {
                    if (linkGroup.get_Parameter(adskGparamGuid).HasValue)
                    {
                        gSet = linkGroup.get_Parameter(adskGparamGuid).AsString();
                    }
                }

                //ищем задание в текущей модели
                int i = 0; int j = 0;
                if (groups == null || groups.Count == 0)
                {
                    status = "Задание еще не вставлялось.";
                    if (badElementIds.Length > 0) status += " Не у всех элементов в задании заполнена позиция (Марка).";
                    if (k > 0) status += " Не все элементы согласованы КР или BIM.";
                    string groupText0 = shortName + "=" + status + "=" + gSet;
                    groupTxtList.Add(groupText0); Logger.Log(groupText0, 1);
                    continue;
                }
                else
                {
                    foreach (var group in groups)
                    {
                        status = "";
                        string[] nameParts1 = group.Name.Split('_');
                        string shortName1 = group.Name;
                        if (nameParts1.Length > 2) shortName1 = nameParts1[0] + '_' + nameParts1[1] + '_' + nameParts1[2]; //учет групп, созданных по старой концепции
                        if (nameParts.Length < 3) status = "Некорректное имя группы в модели задания.";
                        if (shortName1 == shortName)
                        {
                            i++;
                            //углубленный анализ
                            List<Hole> holes = HolesInGroup(linkDoc, doc, shortName,true);

                            foreach (var hole in holes)
                            {
                                if (hole.status.Length > 0) j++;
                            }
                            if (status == "" || status == "Некорректное имя группы в модели задания.")
                            { if (j > 0) status += "Есть проблемы - см. детальный анализ. "; }
                            if (status.Length > 0) Logger.Log("      статус " + status, 2);
                            break;
                        }
                    }
                    if (i == 0) status = "Задание еще не вставлялось.";
                    if (badElementIds.Length > 0) status += " Не у всех элементов в задании заполнена позиция (Марка).";
                    if (k > 0) status += " Не все элементы согласованы КР или BIM.";
                    if (status == "") status = "Актуально.";
                    string groupText = shortName + "=" + status + "=" + gSet;
                    groupTxtList.Add(groupText); Logger.Log(groupText, 1);

                }


            }
            string groups1 = "";
            foreach (string group in groupTxtList) groups1 += group + "|";

            return groups1;
        }
        public static bool CompareWithTolerance(double a, double b)
        {
            return Math.Abs(a - b) <= 0.02;
        }
        public static List<Hole> HolesInGroup(in Document linkDoc, in Document doc, in string groupName, in bool writeLogs)
        {
            List<Hole> holes = new List<Hole>(); //универсальный класс для отверстий и других заданий

            //параметры
            BuiltInParameter mrk = BuiltInParameter.ALL_MODEL_MARK; //Марка
            BuiltInParameter gm = BuiltInParameter.ALL_MODEL_MODEL; //параметр Группа модели
            ElementId familyNameParamId = new ElementId(-1002002); //id параметра Имя семейства

            Guid adskHoleWidthParamGuid = new Guid("096bc30e-3c95-4637-84d5-9f6bf45d8676");//ADSK_Отверстие_Ширина
            Guid adskHoleHeightParamGuid = new Guid("bc4e92d8-db66-4e93-8923-3af6e2dc8599");//ADSK_Отверстие_Высота
            Guid adskDiamParamGuid = new Guid("9b679ab7-ea2e-49ce-90ab-0549d5aa36ff");//ADSK_Размер_Диаметр
            Guid adskWidthParamGuid = new Guid("8f2e4f93-9472-4941-a65d-0ac468fd6a5d");//ADSK_Размер_Ширина
            Guid adskHeightParamGuid = new Guid("da753fe3-ecfa-465b-9a2c-02f55d0c2ff1");//ADSK_Размер_Высота
            Guid adskLengthParamGuid = new Guid("748a2515-4cc9-4b74-9a69-339a8d65a212");//ADSK_Размер_Длина
            Guid NTaskApprovedBIMParamGuid = new Guid("94587b6e-5bdd-4fe8-bea4-4996c32801c4");//N_Согласовано BIM
            Guid NTaskApprovedSTParamGuid = new Guid("7cb33aa5-8106-4e4c-8038-6691e34f438c");//N_Согласовано КР
            //04.2026: параметр N_Согласовано рук исключен как устаревший, для совместимости заполняется как "1"

            List<RevitLinkInstance> links = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RvtLinks)
            .WhereElementIsNotElementType()
            .Cast<RevitLinkInstance>()
            .ToList();
            List<RevitLinkInstance> taskLinks = new List<RevitLinkInstance>();
            foreach (RevitLinkInstance link in links) if (link.Name.Contains("Задани") || link.Name.Contains("задани") || link.Name.Contains("-ЗД") || link.Name.Contains("_ЗД") || link.Name.Contains("ЗАДАНИЕ")) taskLinks.Add(link);

            List<Group> linkGroups = new FilteredElementCollector(linkDoc).OfCategory(BuiltInCategory.OST_IOSModelGroups)
                .WhereElementIsNotElementType()
                .Cast<Group>()
                .ToList();

            List<Group> groups = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_IOSModelGroups)
                .WhereElementIsNotElementType()
            .Cast<Group>()
            .ToList();

            if (writeLogs) Logger.Log("   Детальный анализ:", 2);
            //проходим по группам в связанной модели
            foreach (var linkGroup in linkGroups)
            {
                string[] nameParts = linkGroup.Name.Split('_');
                string shortName = linkGroup.Name;

                if (nameParts.Length > 2) shortName = nameParts[0] + '_' + nameParts[1] + '_' + nameParts[2];
                if (shortName == groupName)
                {
                    if (writeLogs) Logger.Log("Группа для обработки: " + shortName, 2);
                    //отверстия группы в связанной модели
                    ElementFilter elementFilter = (ElementFilter)new ElementParameterFilter(ParameterFilterRuleFactory.CreateContainsRule(familyNameParamId, "pmN.Отверстие", true));
                    IList<ElementId> linkGroupElems = linkGroup.GetDependentElements(elementFilter);
                    //прочие задания группы в связанной модели
                    ElementFilter elementFilter21 = (ElementFilter)new ElementParameterFilter(ParameterFilterRuleFactory.CreateContainsRule(familyNameParamId, "pmN.Рама под оборудование", true));
                    ElementFilter elementFilter22 = (ElementFilter)new ElementParameterFilter(ParameterFilterRuleFactory.CreateContainsRule(familyNameParamId, "pmN.Задание на шахту", true));
                    ElementFilter elementFilter23 = (ElementFilter)new ElementParameterFilter(ParameterFilterRuleFactory.CreateContainsRule(familyNameParamId, "pmN.Задание на приямок", true));
                    List<ElementId> linkGroupElems21 = linkGroup.GetDependentElements(elementFilter21).ToList();
                    List<ElementId> linkGroupElems22 = linkGroup.GetDependentElements(elementFilter22).ToList();
                    List<ElementId> linkGroupElems23 = linkGroup.GetDependentElements(elementFilter23).ToList();
                    List<ElementId> linkGroupElems2 = linkGroupElems21.Union(linkGroupElems22).Union(linkGroupElems23).ToList();

                    if (groups == null || groups.Count == 0) continue;
                    //ищем задание в текущей модели
                    foreach (var group in groups)
                    {
                        string[] nameParts1 = group.Name.Split('_');
                        string shortName1 = group.Name;
                        if (nameParts1.Length > 2) shortName1 = nameParts1[0] + '_' + nameParts1[1] + '_' + nameParts1[2];
                        if (shortName1 == shortName)
                        {
                            if (writeLogs) Logger.Log("Группа " + shortName + " найдена в текущей модели", 2);
                            //элементы группы в текущей модели
                            IList<ElementId> groupElems = group.GetDependentElements(elementFilter);
                            List<ElementId> groupElemsToDelete = groupElems.ToList();
                            List<ElementId> groupElems21 = group.GetDependentElements(elementFilter21).ToList();
                            List<ElementId> groupElems22 = group.GetDependentElements(elementFilter22).ToList();
                            List<ElementId> groupElemsToDelete2 = groupElems21.Union(groupElems22).ToList();
                            List<ElementId> groupElems2 = groupElems21.Union(groupElems22).ToList();


                            //отверстия
                            foreach (ElementId linkGroupElem in linkGroupElems)
                            {
                                Element linkElem = linkDoc.GetElement(linkGroupElem);
                                string linkElem_mark = linkElem.get_Parameter(mrk).AsValueString();
                                if (linkElem_mark == null || linkElem_mark.Length == 0) { continue; } //пропускаем отверстия с незаполненной Маркой
                                else
                                {
                                    if (writeLogs) Logger.Log("Отверстие " + linkElem_mark + ":", 2);
                                    string linkElem_status = "";
                                    bool linkElem_pasted = false;

                                    Guid widthParam = adskHoleWidthParamGuid; Guid heightParam = adskHoleHeightParamGuid;
                                    bool circleHole = false; string widthParamName = "Ширина: "; string heightParamName = "Высота: ";
                                    foreach (Parameter param in linkElem.ParametersMap) //круглые отв
                                    {
                                        if (param.IsShared)
                                        {
                                            Guid paramGUID = param.GUID;
                                            if (paramGUID == adskDiamParamGuid)
                                            {
                                                circleHole = true; widthParamName = "Диаметр: "; heightParamName = "Диаметр: ";
                                                widthParam = adskDiamParamGuid; heightParam = adskDiamParamGuid;
                                                if (writeLogs) Logger.Log("   круглое", 2);
                                                break;
                                            }
                                        }

                                    }
                                    double linkElem_width = linkElem.get_Parameter(widthParam).AsDouble() * 0.3048 * 1000;
                                    double linkElem_height = linkElem.get_Parameter(heightParam).AsDouble() * 0.3048 * 1000;
                                    if (writeLogs) Logger.Log("   " + widthParamName + linkElem_width.ToString(), 2);
                                    if (writeLogs) Logger.Log("   " + heightParamName + linkElem_width.ToString(), 2);

                                    string linkElem_coordStatusHead = "1";
                                    linkElem_coordStatusHead = "v";
                                        

                                    string linkElem_coordStatusBIM = "-";
                                    int linkElem_coordStatusBIM_int = linkElem.get_Parameter(NTaskApprovedBIMParamGuid).AsInteger();
                                    if (linkElem_coordStatusBIM_int == 1)
                                    {
                                        linkElem_coordStatusBIM = "v";
                                        if (writeLogs) Logger.Log("   согласовано BIM", 2);
                                    }

                                    string linkElem_coordStatusST = "-";
                                    int linkElem_coordStatusST_int = linkElem.get_Parameter(NTaskApprovedSTParamGuid).AsInteger();
                                    if (linkElem_coordStatusST_int == 1)
                                    {
                                        linkElem_coordStatusST = "v";
                                        if (writeLogs) Logger.Log("   согласовано КР", 2);
                                    }
                                    else linkElem_status += "Не согласовано КР. ";

                                    LocationPoint linkElem_lp = (LocationPoint)linkElem.Location;
                                    XYZ p = linkElem_lp.Point;

                                    foreach (var link in taskLinks)
                                    {
                                        var transform = link.GetTransform(); p = transform.OfPoint(p); break;
                                    }

                                    double linkElem_x = p.X * 0.3048; double linkElem_y = p.Y * 0.3048; double linkElem_z = p.Z * 0.3048;
                                    linkElem_x = Math.Round(linkElem_x, 3); linkElem_y = Math.Round(linkElem_y, 3); linkElem_z = Math.Round(linkElem_z, 3);
                                    if (writeLogs) Logger.Log("   Х: " + linkElem_x.ToString() + " Y: " + linkElem_y.ToString() + " Z: " + linkElem_z.ToString(), 2);

                                    int id1 = 0;

                                    foreach (ElementId groupElem in groupElems)
                                    {
                                        Element elem = doc.GetElement(groupElem);
                                        string elem_mark = elem.get_Parameter(mrk).AsValueString();
                                        if (elem_mark == linkElem_mark) //нашли отверстие с той же маркой
                                        {
                                            linkElem_pasted = true;
                                            id1 = groupElem.IntegerValue;
                                            if (writeLogs) Logger.Log("   найдено в текущей модели", 2);

                                            int d = 0;
                                            foreach (Parameter param in elem.ParametersMap) //круглые отв
                                            {
                                                if (param.IsShared)
                                                {
                                                    Guid paramGUID = param.GUID;
                                                    if (paramGUID == adskDiamParamGuid)
                                                    {
                                                        d++;
                                                        if (writeLogs) Logger.Log("   круглое", 2);
                                                        break;
                                                    }
                                                }

                                            }
                                            if (circleHole && d == 0) //в Задании - круглое, а в КЖ - нет
                                            {
                                                linkElem_status += "Отверстие в задании изменено на круглое. ";
                                                break;
                                            }
                                            if (!circleHole && d > 0) //в Задании - прямоугольное, а в КЖ - круглое
                                            {
                                                linkElem_status += "Отверстие в задании изменено на прямоугольное. ";
                                                break;
                                            }

                                            double elem_width = elem.get_Parameter(widthParam).AsDouble() * 0.3048 * 1000;
                                            if (CompareWithTolerance(elem_width, linkElem_width) == false) linkElem_status += widthParamName + elem_width.ToString() + ". ";

                                            if (writeLogs) Logger.Log("      " + elem.get_Parameter(widthParam).Definition.Name + ": " + elem_width.ToString() + "; исходное: " + linkElem_width.ToString(), 2);

                                            if (!circleHole)
                                            {
                                                double elem_height = elem.get_Parameter(heightParam).AsDouble() * 0.3048 * 1000;
                                                if (CompareWithTolerance(elem_height, linkElem_height) == false) linkElem_status += "Высота: " + elem_height.ToString() + ". ";
                                                if (writeLogs) Logger.Log("      " + elem.get_Parameter(heightParam).Definition.Name + ": " + elem_height.ToString() + "; исходное: " + linkElem_height.ToString(), 2);

                                            }

                                            LocationPoint elem_lp = (LocationPoint)elem.Location;
                                            XYZ point = elem_lp.Point; //XYZ point1 = transform.OfPoint(point);
                                            double elem_x = point.X * 0.3048; double elem_y = point.Y * 0.3048; double elem_z = point.Z * 0.3048;
                                            elem_x = Math.Round(elem_x, 3); elem_y = Math.Round(elem_y, 3); elem_z = Math.Round(elem_z, 3);
                                            if (CompareWithTolerance(linkElem_x, elem_x) == false) linkElem_status += "X: " + elem_x.ToString() + ". ";
                                            if (CompareWithTolerance(linkElem_y, elem_y) == false) linkElem_status += "Y: " + elem_y.ToString() + ". ";
                                            if (CompareWithTolerance(linkElem_z, elem_z) == false) linkElem_status += "Z: " + elem_z.ToString() + ". ";

                                            if (writeLogs) Logger.Log("      " + "X: " + elem_x.ToString() + "; Y: " + elem_y.ToString() + "; Z: " +
                                                elem_z.ToString() + "; исходное: " + "X: " + linkElem_x.ToString() + "; Y: " + linkElem_y.ToString() + "; Z: " + linkElem_z.ToString(), 2);


                                            if (writeLogs) Logger.Log("   удаляем из списка на удаление", 2);
                                            groupElemsToDelete.Remove(groupElem);
                                            break;
                                        }
                                    }
                                    if (!linkElem_pasted) //если отверстие не найдено в группе
                                    {
                                        int holesOutOfGroupCount = 0;

                                        List<FamilyInstance> GMs = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_GenericModel)   //фильтр по категории Об модели
                                                                         .WhereElementIsNotElementType()
                                                                         .OfClass(typeof(FamilyInstance))
                                                                         .Cast<FamilyInstance>()
                                                                         .ToList();
                                        List<FamilyInstance> holesGM = new List<FamilyInstance>();

                                        foreach (FamilyInstance GM in GMs)
                                        {
                                            string gmvalue = GM.Symbol.get_Parameter(gm).AsString();
                                            if (gmvalue != null)
                                            {
                                                if (gmvalue.Contains("Отверстие")) holesGM.Add(GM);
                                            }
                                        }
                                        foreach (FamilyInstance hole1 in holesGM)
                                        {
                                            Element elem = (Element)hole1;
                                            string elem_mark = elem.get_Parameter(mrk).AsValueString();
                                            if (elem_mark != null)
                                            {
                                                if (elem_mark == linkElem_mark)
                                                {
                                                    linkElem_pasted = true;
                                                    id1 = elem.Id.IntegerValue;
                                                    if (writeLogs) Logger.Log("   найдено в текущей модели", 2);

                                                    double elem_width = elem.get_Parameter(widthParam).AsDouble() * 0.3048 * 1000;
                                                    if (CompareWithTolerance(elem_width, linkElem_width) == false) linkElem_status += widthParamName + elem_width.ToString() + ". ";

                                                    if (!circleHole)
                                                    {
                                                        double elem_height = elem.get_Parameter(heightParam).AsDouble() * 0.3048 * 1000;
                                                        if (CompareWithTolerance(elem_height, linkElem_height) == false) linkElem_status += "Высота: " + elem_height.ToString() + ". ";
                                                    }

                                                    LocationPoint elem_lp = (LocationPoint)elem.Location;
                                                    XYZ point = elem_lp.Point;

                                                    foreach (var link in taskLinks)
                                                    {
                                                        var transform = link.GetTransform(); point = transform.OfPoint(point); break;
                                                    }

                                                    double elem_x = point.X * 0.3048; double elem_y = point.Y * 0.3048; double elem_z = point.Z * 0.3048;
                                                    elem_x = Math.Round(elem_x, 3); elem_y = Math.Round(elem_y, 3); elem_z = Math.Round(elem_z, 3);
                                                    if (CompareWithTolerance(linkElem_x, elem_x) == false) linkElem_status += "X: " + elem_x.ToString() + ". ";
                                                    if (CompareWithTolerance(linkElem_y, elem_y) == false) linkElem_status += "Y: " + elem_y.ToString() + ". ";
                                                    if (CompareWithTolerance(linkElem_z, elem_z) == false) linkElem_status += "Z: " + elem_z.ToString() + ". ";

                                                    if (writeLogs) Logger.Log("   удаляем из списка на удаление", 2);
                                                    groupElemsToDelete.Remove(elem.Id);

                                                    linkElem_status += "Вставлено вне группы. ";

                                                    holesOutOfGroupCount++; break;
                                                }
                                            }
                                        }

                                        if (holesOutOfGroupCount == 0) linkElem_status += "Не вставлено. ";

                                        if (writeLogs) Logger.Log("   Статус: " + linkElem_status, 2);
                                    }


                                    if (writeLogs) Logger.Log("   создаем Hole", 2);
                                    //Hole
                                    Hole hole = new Hole()
                                    {
                                        pasted = linkElem_pasted,
                                        mark = linkElem_mark,
                                        mark1 = linkElem_mark,
                                        length = 0,
                                        width = linkElem_width,
                                        height = linkElem_height,
                                        coordStatusHead = linkElem_coordStatusHead,
                                        coordStatusBIM = linkElem_coordStatusBIM,
                                        coordStatusST = linkElem_coordStatusST,
                                        x = linkElem_x,
                                        y = linkElem_y,
                                        z = linkElem_z,
                                        status = linkElem_status,
                                        id1 = id1,
                                    };
                                    holes.Add(hole);

                                }


                            }
                            //другие задания
                            foreach (ElementId linkGroupElem in linkGroupElems2)
                            {
                                Element linkElem = linkDoc.GetElement(linkGroupElem);
                                string linkElem_mark = linkElem.get_Parameter(mrk).AsValueString();
                                if (linkElem_mark == null || linkElem_mark.Length == 0) { continue; } //пропускаем отверстия с незаполненной Маркой
                                else
                                {
                                    if (writeLogs) Logger.Log("Задание " + linkElem_mark + ":", 2);
                                    string linkElem_status = "";
                                    bool linkElem_pasted = false;

                                    string lengthParamName = "Длина: "; string widthParamName = "Ширина: "; string heightParamName = "Высота: ";

                                    double linkElem_length = linkElem.get_Parameter(adskLengthParamGuid).AsDouble() * 0.3048 * 1000;
                                    double linkElem_width = linkElem.get_Parameter(adskWidthParamGuid).AsDouble() * 0.3048 * 1000;
                                    double linkElem_height = linkElem.get_Parameter(adskHeightParamGuid).AsDouble() * 0.3048 * 1000;
                                    if (writeLogs) Logger.Log("   " + lengthParamName + linkElem_length.ToString(), 2);
                                    if (writeLogs) Logger.Log("   " + widthParamName + linkElem_width.ToString(), 2);
                                    if (writeLogs) Logger.Log("   " + heightParamName + linkElem_width.ToString(), 2);

                                    string linkElem_coordStatusHead = "1";
                                    linkElem_coordStatusHead = "v";

                                    string linkElem_coordStatusBIM = "-";
                                    int linkElem_coordStatusBIM_int = linkElem.get_Parameter(NTaskApprovedBIMParamGuid).AsInteger();
                                    if (linkElem_coordStatusBIM_int == 1)
                                    {
                                        linkElem_coordStatusBIM = "v";
                                        if (writeLogs) Logger.Log("   согласовано BIM", 2);
                                    }

                                    string linkElem_coordStatusST = "-";
                                    int linkElem_coordStatusST_int = linkElem.get_Parameter(NTaskApprovedSTParamGuid).AsInteger();
                                    if (linkElem_coordStatusST_int == 1)
                                    {
                                        linkElem_coordStatusST = "v";
                                        if (writeLogs) Logger.Log("   согласовано КР", 2);
                                    }
                                    else linkElem_status += "Не согласовано КР. ";

                                    LocationPoint linkElem_lp = (LocationPoint)linkElem.Location;
                                    XYZ p = linkElem_lp.Point;

                                    foreach (var link in taskLinks)
                                    {
                                        var transform = link.GetTransform(); p = transform.OfPoint(p); break;
                                    }

                                    double linkElem_x = p.X * 0.3048; double linkElem_y = p.Y * 0.3048; double linkElem_z = p.Z * 0.3048;
                                    linkElem_x = Math.Round(linkElem_x, 3); linkElem_y = Math.Round(linkElem_y, 3); linkElem_z = Math.Round(linkElem_z, 3);
                                    if (writeLogs) Logger.Log("   Х: " + linkElem_x.ToString() + " Y: " + linkElem_y.ToString() + " Z: " + linkElem_z.ToString(), 2);

                                    int id1 = 0;

                                    foreach (ElementId groupElem in groupElems2)
                                    {
                                        Element elem = doc.GetElement(groupElem);
                                        string elem_mark = elem.get_Parameter(mrk).AsValueString();
                                        if (elem_mark == linkElem_mark) //нашли задание с той же маркой
                                        {
                                            linkElem_pasted = true;
                                            id1 = groupElem.IntegerValue;
                                            if (writeLogs) Logger.Log("   найдено в текущей модели", 2);

                                            double elem_length = elem.get_Parameter(adskLengthParamGuid).AsDouble() * 0.3048 * 1000;
                                            if (CompareWithTolerance(elem_length, linkElem_length) == false) linkElem_status += lengthParamName + elem_length.ToString() + ". ";
                                            if (writeLogs) Logger.Log("      " + elem.get_Parameter(adskLengthParamGuid).Definition.Name + ": " + elem_length.ToString() + "; исходное: " + linkElem_length.ToString(), 2);

                                            double elem_width = elem.get_Parameter(adskWidthParamGuid).AsDouble() * 0.3048 * 1000;
                                            if (CompareWithTolerance(elem_width, linkElem_width) == false) linkElem_status += widthParamName + elem_width.ToString() + ". ";
                                            if (writeLogs) Logger.Log("      " + elem.get_Parameter(adskWidthParamGuid).Definition.Name + ": " + elem_width.ToString() + "; исходное: " + linkElem_width.ToString(), 2);

                                            double elem_height = elem.get_Parameter(adskHeightParamGuid).AsDouble() * 0.3048 * 1000;
                                            if (CompareWithTolerance(elem_height, linkElem_height) == false) linkElem_status += "Высота: " + elem_height.ToString() + ". ";
                                            if (writeLogs) Logger.Log("      " + elem.get_Parameter(adskHeightParamGuid).Definition.Name + ": " + elem_height.ToString() + "; исходное: " + linkElem_height.ToString(), 2);



                                            LocationPoint elem_lp = (LocationPoint)elem.Location;
                                            XYZ point = elem_lp.Point; //XYZ point1 = transform.OfPoint(point);
                                            double elem_x = point.X * 0.3048; double elem_y = point.Y * 0.3048; double elem_z = point.Z * 0.3048;
                                            elem_x = Math.Round(elem_x, 3); elem_y = Math.Round(elem_y, 3); elem_z = Math.Round(elem_z, 3);
                                            if (CompareWithTolerance(linkElem_x, elem_x) == false) linkElem_status += "X: " + elem_x.ToString() + ". ";
                                            if (CompareWithTolerance(linkElem_y, elem_y) == false) linkElem_status += "Y: " + elem_y.ToString() + ". ";
                                            if (CompareWithTolerance(linkElem_z, elem_z) == false) linkElem_status += "Z: " + elem_z.ToString() + ". ";

                                            if (writeLogs) Logger.Log("      " + "X: " + elem_x.ToString() + "; Y: " + elem_y.ToString() + "; Z: " + elem_z.ToString() + "; исходное: " +
                                                "X: " + linkElem_x.ToString() + "; Y: " + linkElem_y.ToString() + "; Z: " + linkElem_z.ToString(), 2);


                                            if (writeLogs) Logger.Log("   удаляем из списка на удаление", 2);
                                            groupElemsToDelete2.Remove(groupElem);
                                            break;
                                        }
                                    }
                                    if (!linkElem_pasted) //если задание не найдено в группе
                                    {
                                        int holesOutOfGroupCount = 0;

                                        List<FamilyInstance> GMs = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_GenericModel)   //фильтр по категории Об модели
                                                                         .WhereElementIsNotElementType()
                                                                         .OfClass(typeof(FamilyInstance))
                                                                         .Cast<FamilyInstance>()
                                                                         .ToList();
                                        List<FamilyInstance> holesGM = new List<FamilyInstance>();

                                        foreach (FamilyInstance GM in GMs)
                                        {
                                            string gmvalue = GM.Symbol.get_Parameter(gm).AsString();
                                            if (gmvalue != null)
                                            {
                                                if (gmvalue.Contains("Рама под оборудование")) holesGM.Add(GM);
                                            }
                                        }
                                        foreach (FamilyInstance hole1 in holesGM)
                                        {
                                            Element elem = (Element)hole1;
                                            string elem_mark = elem.get_Parameter(mrk).AsValueString();
                                            if (elem_mark != null)
                                            {
                                                if (elem_mark == linkElem_mark)
                                                {
                                                    linkElem_pasted = true;
                                                    id1 = elem.Id.IntegerValue;
                                                    if (writeLogs) Logger.Log("   найдено в текущей модели", 2);

                                                    double elem_length = elem.get_Parameter(adskLengthParamGuid).AsDouble() * 0.3048 * 1000;
                                                    if (CompareWithTolerance(elem_length, linkElem_length) == false) linkElem_status += lengthParamName + elem_length.ToString() + ". ";

                                                    double elem_width = elem.get_Parameter(adskWidthParamGuid).AsDouble() * 0.3048 * 1000;
                                                    if (CompareWithTolerance(elem_width, linkElem_width) == false) linkElem_status += widthParamName + elem_width.ToString() + ". ";

                                                    double elem_height = elem.get_Parameter(adskHeightParamGuid).AsDouble() * 0.3048 * 1000;
                                                    if (CompareWithTolerance(elem_height, linkElem_height) == false) linkElem_status += "Высота: " + elem_height.ToString() + ". ";

                                                    LocationPoint elem_lp = (LocationPoint)elem.Location;
                                                    XYZ point = elem_lp.Point;

                                                    foreach (var link in taskLinks)
                                                    {
                                                        var transform = link.GetTransform(); point = transform.OfPoint(point); break;
                                                    }

                                                    double elem_x = point.X * 0.3048; double elem_y = point.Y * 0.3048; double elem_z = point.Z * 0.3048;
                                                    elem_x = Math.Round(elem_x, 3); elem_y = Math.Round(elem_y, 3); elem_z = Math.Round(elem_z, 3);
                                                    if (CompareWithTolerance(linkElem_x, elem_x) == false) linkElem_status += "X: " + elem_x.ToString() + ". ";
                                                    if (CompareWithTolerance(linkElem_y, elem_y) == false) linkElem_status += "Y: " + elem_y.ToString() + ". ";
                                                    if (CompareWithTolerance(linkElem_z, elem_z) == false) linkElem_status += "Z: " + elem_z.ToString() + ". ";

                                                    if (writeLogs) Logger.Log("   удаляем из списка на удаление", 2);
                                                    groupElemsToDelete2.Remove(elem.Id);

                                                    linkElem_status += "Вставлено вне группы. ";

                                                    holesOutOfGroupCount++; break;
                                                }
                                            }
                                        }

                                        if (holesOutOfGroupCount == 0) linkElem_status += "Не вставлено. ";

                                        if (writeLogs) Logger.Log("   Статус: " + linkElem_status, 2);
                                    }


                                    if (writeLogs) Logger.Log("   создаем Hole", 2);
                                    //Hole
                                    Hole hole = new Hole()
                                    {
                                        pasted = linkElem_pasted,
                                        mark = linkElem_mark,
                                        mark1 = linkElem_mark,
                                        length = linkElem_length,
                                        width = linkElem_width,
                                        height = linkElem_height,
                                        coordStatusHead = linkElem_coordStatusHead,
                                        coordStatusBIM = linkElem_coordStatusBIM,
                                        coordStatusST = linkElem_coordStatusST,
                                        x = linkElem_x,
                                        y = linkElem_y,
                                        z = linkElem_z,
                                        status = linkElem_status,
                                        id1 = id1,
                                    };
                                    holes.Add(hole);

                                }


                            }

                            //список Hole в текущей модели, отсутствующих в связанной
                            //отверстия
                            if (groupElemsToDelete.Count > 0)
                            {
                                if (writeLogs) Logger.Log("Обрабатываем лишние отверстия:", 2);
                                foreach (var groupElem in groupElemsToDelete)
                                {
                                    Element elem = doc.GetElement(groupElem);
                                    string elem_mark = elem.get_Parameter(mrk).AsValueString();
                                    if (elem_mark == null || elem_mark.Length == 0) elem_mark = "-";
                                    if (writeLogs) Logger.Log("отверстие " + elem_mark + " id: " + elem.Id.ToString(), 2);

                                    Guid widthParam = adskHoleWidthParamGuid; Guid heightParam = adskHoleHeightParamGuid;
                                    foreach (Parameter param in elem.ParametersMap) //круглые отв
                                    {
                                        if (param.IsShared)
                                        {
                                            Guid paramGUID = param.GUID;
                                            if (paramGUID == adskDiamParamGuid)
                                            {
                                                widthParam = adskDiamParamGuid; heightParam = adskDiamParamGuid;
                                                break;
                                            }
                                        }
                                    }
                                    double elem_width = elem.get_Parameter(widthParam).AsDouble() * 0.3048 * 1000;
                                    double elem_height = elem.get_Parameter(widthParam).AsDouble() * 0.3048 * 1000;

                                    if (writeLogs) Logger.Log("   ширина: " + elem_width.ToString(), 2);
                                    if (writeLogs) Logger.Log("   высота: " + elem_height.ToString(), 2);

                                    LocationPoint elem_lp = (LocationPoint)elem.Location;
                                    XYZ point = elem_lp.Point; //XYZ point1 = transform.OfPoint(point);
                                    double elem_x = point.X * 0.3048; double elem_y = point.Y * 0.3048; double elem_z = point.Z * 0.3048;
                                    elem_x = Math.Round(elem_x, 3); elem_y = Math.Round(elem_y, 3); elem_z = Math.Round(elem_z, 3);

                                    if (writeLogs) Logger.Log("   Х: " + elem_x.ToString() + " Y: " + elem_y.ToString() + " Z: " + elem_z.ToString(), 2);

                                    if (writeLogs) Logger.Log("   создаем Hole", 2);
                                    string st = "Лишнее отверстие " + elem_mark + " удалено в Задании.";
                                    //if (scenario == 2) st = "Не заполнена позиция (Марка).";
                                    //Hole
                                    Hole hole = new Hole()
                                    {
                                        pasted = true,
                                        mark = "-",
                                        mark1 = elem_mark,
                                        width = elem_width,
                                        height = elem_height,
                                        coordStatusHead = "-",
                                        coordStatusBIM = "-",
                                        coordStatusST = "-",
                                        x = elem_x,
                                        y = elem_y,
                                        z = elem_z,
                                        status = st,
                                        id1 = groupElem.IntegerValue,
                                    };
                                    holes.Add(hole);
                                }
                            }
                            //другие задания
                            if (groupElemsToDelete2.Count > 0)
                            {
                                if (writeLogs) Logger.Log("Обрабатываем лишние задания:", 2);
                                foreach (var groupElem in groupElemsToDelete2)
                                {
                                    Element elem = doc.GetElement(groupElem);
                                    string elem_mark = elem.get_Parameter(mrk).AsValueString();
                                    if (elem_mark == null || elem_mark.Length == 0) elem_mark = "-";
                                    if (writeLogs) Logger.Log("задание " + elem_mark + " id: " + elem.Id.ToString(), 2);

                                    double elem_length = elem.get_Parameter(adskLengthParamGuid).AsDouble() * 0.3048 * 1000;
                                    double elem_width = elem.get_Parameter(adskWidthParamGuid).AsDouble() * 0.3048 * 1000;
                                    double elem_height = elem.get_Parameter(adskHeightParamGuid).AsDouble() * 0.3048 * 1000;

                                    if (writeLogs) Logger.Log("   длина: " + elem_length.ToString(), 2);
                                    if (writeLogs) Logger.Log("   ширина: " + elem_width.ToString(), 2);
                                    if (writeLogs) Logger.Log("   высота: " + elem_height.ToString(), 2);

                                    LocationPoint elem_lp = (LocationPoint)elem.Location;
                                    XYZ point = elem_lp.Point; //XYZ point1 = transform.OfPoint(point);
                                    double elem_x = point.X * 0.3048; double elem_y = point.Y * 0.3048; double elem_z = point.Z * 0.3048;
                                    elem_x = Math.Round(elem_x, 3); elem_y = Math.Round(elem_y, 3); elem_z = Math.Round(elem_z, 3);

                                    if (writeLogs) Logger.Log("   Х: " + elem_x.ToString() + " Y: " + elem_y.ToString() + " Z: " + elem_z.ToString(), 2);

                                    if (writeLogs) Logger.Log("   создаем Hole", 2);
                                    string st = "Лишний элемент " + elem_mark + " удалено в Задании.";
                                    //if (scenario == 2) st = "Не заполнена позиция (Марка).";
                                    //Hole
                                    Hole hole = new Hole()
                                    {
                                        pasted = true,
                                        mark = "-",
                                        mark1 = elem_mark,
                                        length = elem_length,
                                        width = elem_width,
                                        height = elem_height,
                                        coordStatusHead = "-",
                                        coordStatusBIM = "-",
                                        coordStatusST = "-",
                                        x = elem_x,
                                        y = elem_y,
                                        z = elem_z,
                                        status = st,
                                        id1 = groupElem.IntegerValue,
                                    };
                                    holes.Add(hole);
                                }
                            }


                            break;
                        }
                    }
                    break;
                }
            }
            List<Hole> holesSorted = new List<Hole>();
            foreach (var hole in holes)
            {
                int holeorder = 0;
                if (hole.status.Contains("КР")) holeorder = 4;
                else if (hole.status.Contains("Не вставлено.")) holeorder = 1;
                else if (hole.status.Length == 0) holeorder = 5;
                else if (hole.status.Contains("Лишнее отверстие")) holeorder = 2;
                else holeorder = 3;
                hole.holeorder = holeorder;
                holesSorted.Add(hole);
            }
            return holesSorted;
        }
        public static void SaveGroupsData(in Document doc, in string userName)
        {
            string docName = doc.Title.ToString();
            //имя и роль пользователя
            string userDepartment = "-";
            string[] rolesFile = File.ReadAllLines("//fs-nova/Distr/0.For Admin/_TNov/roles.txt");
            foreach (string role in rolesFile)
            {
                if (role.Contains(userName))
                {
                    string[] line = role.Split(','); userDepartment = line[1]; break;
                }
            }

            docName = docName.Replace(",", " "); string docNameUserName = "_" + userName; docName = docName.Replace(docNameUserName, "");

            List<Group> groups = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_IOSModelGroups)
                .WhereElementIsNotElementType()
                .Cast<Group>()
                .ToList();

            List<string> names = new List<string>();
            foreach (Group group in groups)
            {
                names.Add(group.Name);
            }
            List<HoleGroupBaseItem> existingItems = new List<HoleGroupBaseItem>();
            // Десериализация
            string jsonFilePath = nova.novaserver + "_TNov/tasks/" + docName + ".json";
            if (File.Exists(jsonFilePath))
            {
                string jsonContent = File.ReadAllText(jsonFilePath);
                existingItems = JsonConvert.DeserializeObject<List<HoleGroupBaseItem>>(jsonContent)
                                ?? new List<HoleGroupBaseItem>();
            }
            string currentDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            foreach (Group group in groups)
            {
                string groupName = group.Name;
                if (string.IsNullOrEmpty(groupName))
                    continue;

                string[] shortNameParts = groupName.Split('_');
                string pt1 = shortNameParts[0];
                string pt2 = ""; if (shortNameParts.Length > 1) pt2 = shortNameParts[1];
                string pt3 = ""; if (shortNameParts.Length > 2) pt3 = shortNameParts[2];

                // Ищем запись с таким именем группы
                HoleGroupBaseItem existingItem = existingItems.FirstOrDefault(item => item.HoleGroupName == groupName);

                if (existingItem != null)
                {
                    
                }
                else
                {
                    // Новая группа – создаём запись с версией ""
                    existingItems.Add(new HoleGroupBaseItem
                    {
                        HoleGroupName = groupName,
                        TaskVersion = "",
                        HoleGroupNamePart1 = pt1,
                        HoleGroupNamePart2 = pt2,
                        HoleGroupNamePart3 = pt3
                    });
                }
            }

            string updatedJson = JsonConvert.SerializeObject(existingItems, Formatting.Indented);
            try
            {
                File.WriteAllText(jsonFilePath, updatedJson);
            }
            catch (Exception) { }
        }
        public static int GetHoleMaxNumber(in Document doc)
        {
            //параметры
            BuiltInParameter mrk = BuiltInParameter.ALL_MODEL_MARK; //Марка
            ElementId familyNameParamId = new ElementId(-1002002); //id параметра Имя семейства

            List<FamilyInstance> GMs = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_GenericModel)   //фильтр по категории Об модели
                                                                         .WhereElementIsNotElementType()
                                                                         .OfClass(typeof(FamilyInstance))
                                                                         .Cast<FamilyInstance>()
                                                                         .ToList();
            BuiltInParameter gm = BuiltInParameter.ALL_MODEL_MODEL; //параметр Группа модели

            List<FamilyInstance> holesGM = new List<FamilyInstance>();

            foreach (FamilyInstance GM in GMs) //ищем отверстия об мод
            {
                string gmvalue = GM.Symbol.get_Parameter(gm).AsString();
                if (gmvalue != null)
                {
                    if (gmvalue.Contains("Отверстие")) { holesGM.Add(GM); }
                }

            }
            int maxNumber = 0; string groupName = "-";

            if (holesGM.Count < 1) return 0;// "-";

            foreach (var holeGM in holesGM)
            {
                int elemNum = 0;
                Element elem = doc.GetElement(holeGM.Id);
                string mrkvalue = elem.get_Parameter(mrk).AsValueString();
                if (mrkvalue == null || mrkvalue == "") elemNum = 0;
                int.TryParse(mrkvalue, out elemNum);
                if (elemNum > maxNumber)
                {
                    maxNumber = elemNum;
                    ElementId groupId = elem.GroupId;
                    if (groupId.IntegerValue > 0)
                    {
                        Element g = doc.GetElement(groupId);
                        groupName = g.Name;
                    }
                }
            }

            int res = maxNumber;//.ToString();
                                //if (groupName != "-") res += " в группе " + groupName; else res += " (вне группы)";

            return res;
        }
        public static void CopyGroupFromLink(string name, Document doc)
        {
            List<RevitLinkInstance> links = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RvtLinks)
                                                                         .WhereElementIsNotElementType()
                                                                         .Cast<RevitLinkInstance>()
                                                                         .ToList();

            List<RevitLinkInstance> taskLinks = new List<RevitLinkInstance>(); //пустой список связей заданий

            Logger.Log("Ищем связь задания", 2);

            foreach (var link in links)
            {
                if (link.Name.Contains("Задани") || link.Name.Contains("задани") || link.Name.Contains("-ЗД") || link.Name.Contains("_ЗД") || link.Name.Contains("ЗАДАНИЕ")) taskLinks.Add(link);
            }
            if (taskLinks.Count > 1)
            {
                Logger.Log("Слишком много моделей заданий!", 4);
                new InfoWindow280("Ошибка!\nСвязь задания вставлена больше одного раза, либо вставлено несколько разных связей заданий.\nОставьте только одну связь.").ShowDialog();

            }
            else if (taskLinks.Count == 1)
            {
                // группы в связанной модели задания

                Document linkDoc = taskLinks[0].GetLinkDocument();
                var transform = taskLinks[0].GetTransform();
                List<Group> linkGroups = new FilteredElementCollector(linkDoc).OfCategory(BuiltInCategory.OST_IOSModelGroups)
                    .WhereElementIsNotElementType()
                    .Cast<Group>()
                    .ToList();
                ICollection<ElementId> ids = new HashSet<ElementId>();

                foreach (var linkGroup in linkGroups)
                {
                    string shortName = linkGroup.Name;
                    string[] nameParts = linkGroup.Name.Split('_');
                    if (nameParts.Length > 2) shortName = nameParts[0] + '_' + nameParts[1] + '_' + nameParts[2]; //учет групп, созданных по старой концепции

                    if (shortName == name)
                    {
                        LocationPoint point = (LocationPoint)linkGroup.Location;
                        ids.Add(linkGroup.Id);
                        Logger.Log("Группа найдена", 2);
                        break;
                    }
                }
                CopyPasteOptions copyOptions = new CopyPasteOptions();
                using (Transaction t = new Transaction(doc))
                {

                    t.Start("Задания от ИОС. Вставка группы");
                    ICollection<ElementId> newElemIds = ElementTransformUtils.CopyElements(linkDoc, ids, doc, transform, copyOptions);
                    t.Commit();
                    Logger.Log("Группа вставлена", 1);
                }
            }


        }
        public static void CopyTaskElement(in Document doc, in string Mark)
        {
            BoundingBoxXYZ bb;

            string name = Mark;
            Logger.Log("Марка: " + name, 2);

            BuiltInParameter gm = BuiltInParameter.ALL_MODEL_MODEL; //параметр Группа модели
            BuiltInParameter mrk = BuiltInParameter.ALL_MODEL_MARK; //Марка
            ElementId familyNameParamId = new ElementId(-1002002); //id параметра Имя семейства

            List<RevitLinkInstance> links = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RvtLinks)
                                                                            .WhereElementIsNotElementType()
                                                                            .Cast<RevitLinkInstance>()
                                                                            .ToList();

            List<RevitLinkInstance> taskLinks = new List<RevitLinkInstance>(); //пустой список изменяемых связей

            foreach (var link in links)
            {
                if (link.Name.Contains("Задани") || link.Name.Contains("задани") || link.Name.Contains("-ЗД") || link.Name.Contains("_ЗД") || link.Name.Contains("ЗАДАНИЕ")) taskLinks.Add(link);
            }

            if (taskLinks.Count > 1)
            {
                Logger.Log("Слишком много моделей заданий!", 4);
                new InfoWindow280("Ошибка!\nСвязь задания вставлена больше одного раза, либо вставлено несколько разных связей заданий.\nОставьте только одну связь.").ShowDialog();

            }
            else if (taskLinks.Count == 1)
            {
                Document linkDoc = taskLinks[0].GetLinkDocument(); var transform = taskLinks[0].GetTransform();

                List<Group> linkGroups = new FilteredElementCollector(linkDoc).OfCategory(BuiltInCategory.OST_IOSModelGroups)
                    .WhereElementIsNotElementType()
                    .Cast<Group>()
                    .ToList();

                List<FamilyInstance> GMs = new FilteredElementCollector(linkDoc).OfCategory(BuiltInCategory.OST_GenericModel)   //фильтр по категории Об модели
                                                                         .WhereElementIsNotElementType()
                                                                         .OfClass(typeof(FamilyInstance))
                                                                         .Cast<FamilyInstance>()
                                                                         .ToList();
                List<FamilyInstance> holesGM = new List<FamilyInstance>();

                List<Group> groups = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_IOSModelGroups)
                            .WhereElementIsNotElementType()
                            .Cast<Group>()
                            .ToList();

                foreach (FamilyInstance GM in GMs)
                {
                    string gmvalue = GM.Symbol.get_Parameter(gm).AsString();
                    if (gmvalue != null)
                    {
                        if (gmvalue.Contains("Отверстие")) holesGM.Add(GM);
                        else if (gmvalue.Contains("Рама")) holesGM.Add(GM);
                    }
                }
                ElementId elementId = new ElementId(0);
                foreach (FamilyInstance hole in holesGM)
                {
                    Element elem = (Element)hole;
                    string elem_mark = elem.get_Parameter(mrk).AsValueString();
                    if (elem_mark == name)
                    {
                        elementId = elem.Id; break;
                    }
                }

                if (elementId.IntegerValue != 0)
                {
                    string groupName = "";
                    Group targetGroupInLink = null;

                    // Находим группу в связанной модели
                    foreach (var linkGroup in linkGroups) //ищем группу в связанной модели
                    {
                        string[] nameParts = linkGroup.Name.Split('_');
                        string shortName = linkGroup.Name;
                        if (nameParts.Length > 2) shortName = nameParts[0] + '_' + nameParts[1] + '_' + nameParts[2];

                        //элементы группы в связанной модели
                        ElementFilter elementFilter = (ElementFilter)new ElementParameterFilter(ParameterFilterRuleFactory.CreateContainsRule(familyNameParamId, "pmN", true));
                        IList<ElementId> linkGroupElems = linkGroup.GetDependentElements(elementFilter);

                        foreach (ElementId linkGroupElem in linkGroupElems)
                        {
                            Element linkElem = linkDoc.GetElement(linkGroupElem);
                            string linkElem_mark = linkElem.get_Parameter(mrk).AsValueString();
                            if (linkElem_mark == name)
                            {
                                groupName = shortName;
                                targetGroupInLink = linkGroup;
                                Logger.Log("Группа: " + groupName, 2);
                                break;
                            }
                        }
                        if (targetGroupInLink != null) break;
                    }

                    // Находим соответствующую группу в текущей модели
                    Group targetGroupInCurrentModel = null;
                    foreach (var group in groups) //ищем группу в текущей модели
                    {
                        string[] nameParts = group.Name.Split('_');
                        string shortName = group.Name;
                        if (nameParts.Length > 2) shortName = nameParts[0] + '_' + nameParts[1] + '_' + nameParts[2];

                        if (shortName == groupName)
                        {
                            targetGroupInCurrentModel = group; Logger.Log("Группа в текущей модели найдена", 2);
                            break;
                        }
                    }

                    //копируем отверстие в модель
                    ICollection<ElementId> ids = new HashSet<ElementId>();
                    ids.Add(elementId);
                    string gIds = "";
                    CopyPasteOptions copyOptions2 = new CopyPasteOptions();
                    ICollection<ElementId> newElemIds = new List<ElementId>();

                    using (Transaction t = new Transaction(doc))
                    {
                        t.Start("Задания от ИОС. Добавление элемента");
                        Logger.Log("Открываем транзакцию (добавление элемента)", 1);
                        newElemIds = ElementTransformUtils.CopyElements(linkDoc, ids, doc, transform, copyOptions2);
                        List<BoundingBoxXYZ> boxes = new List<BoundingBoxXYZ>();

                        Element elem1 = doc.GetElement(newElemIds.First());
                        elem1.LookupParameter("Марка")?.Set(name);

                        // Добавляем элемент в группу
                        if (targetGroupInCurrentModel != null)
                        {
                            try
                            {
                                // Проверяем, сколько групп с таким GroupType существует
                                GroupType groupType = targetGroupInCurrentModel.GroupType;
                                var groupsWithSameType = new FilteredElementCollector(doc)
                                    .OfClass(typeof(Group))
                                    .WhereElementIsNotElementType()
                                    .Cast<Group>()
                                    .Where(g => g.GroupType.Id == groupType.Id)
                                    .ToList();

                                // Если групп больше одной - ошибка
                                if (groupsWithSameType.Count > 1)
                                {
                                    Logger.Log($"Найдено {groupsWithSameType.Count} групп с типом {groupType.Name}. Должна быть только одна.", 4);
                                    new InfoWindow280($"Ошибка!\nНайдено несколько групп с типом '{groupType.Name}'.\nВ модели должна быть только одна группа с таким типом.").ShowDialog();
                                    throw new Exception("Найдено несколько групп с одинаковым типом");
                                }

                                // Получаем полное имя группы (с номером, если есть)
                                string fullGroupName = targetGroupInCurrentModel.Name;

                                // Разгруппируем группу (это сохранит элементы)
                                IList<ElementId> ungroupedElementIds = targetGroupInCurrentModel.UngroupMembers().ToList();
                                Logger.Log("Группа разгруппирована", 2);

                                // Удаляем все GroupType с таким именем из модели
                                // Собираем все GroupType с таким именем
                                var allGroupTypes = new FilteredElementCollector(doc)
                                    .OfClass(typeof(GroupType))
                                    .Cast<GroupType>()
                                    .Where(gt => gt.Name == fullGroupName)
                                    .ToList();

                                // Удаляем найденные GroupType
                                List<ElementId> groupTypesToDelete = new List<ElementId>();
                                foreach (var gt in allGroupTypes)
                                {
                                    groupTypesToDelete.Add(gt.Id);
                                }

                                if (groupTypesToDelete.Count > 0)
                                {
                                    doc.Delete(groupTypesToDelete);
                                }
                                Logger.Log("Группы в Диспетчере проекта подчищены", 2);

                                // Создаем список элементов для новой группы
                                List<ElementId> elementsForNewGroup = new List<ElementId>(ungroupedElementIds);
                                elementsForNewGroup.Add(newElemIds.First());

                                // Создаем новую группу
                                Group newGroup = doc.Create.NewGroup(elementsForNewGroup);
                                Logger.Log("Создаем новую группу", 2);

                                // Обновляем ссылку на группу
                                targetGroupInCurrentModel = doc.GetElement(newGroup.Id) as Group;

                                // Устанавливаем имя новой группы

                                // Находим все GroupType в проекте
                                FilteredElementCollector collector = new FilteredElementCollector(doc);
                                ICollection<Element> groupTypes = collector
                                    .OfClass(typeof(GroupType))
                                    .ToElements();
                                foreach (GroupType groupType1 in groupTypes)
                                {
                                    if (groupType1.Name == newGroup.Name)
                                    {
                                        groupType1.Name = fullGroupName; Logger.Log("Имя группы изменено", 2);
                                        break;
                                    }
                                }
                                Logger.Log("Элемент добавлен в группу: " + fullGroupName, 1);
                            }
                            catch (Exception ex)
                            {
                                Logger.Log("Ошибка при добавлении в группу: " + ex.Message, 4);
                            }
                        }

                        BoundingBoxXYZ elem1_box = elem1.get_BoundingBox(doc.ActiveView);
                        boxes.Add(elem1_box);
                        
                        bb = boxes.Aggregate((acc, elem2) => acc._BbUnion(elem2));

                        t.Commit(); Logger.Log("Закрываем транзакцию", 1);

                        //new InfoWindow280("Успешно!").ShowDialog();
                    }

                    //Открытие 3D-вида
                    Autodesk.Revit.DB.View3D view3d;

                    List<Autodesk.Revit.DB.View> views = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Views)   //фильтр по категории Виды
                                                                                    .WhereElementIsNotElementType()    //фильтр только экземпляры
                                                                                    .Cast<Autodesk.Revit.DB.View>()                     //элементы категории Виды
                                                                                    .ToList();                         //формируем список
                    UIDocument uidoc = RevitAPI.UiDocument;
                    UIApplication uiApp = RevitAPI.UiApplication; Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
                    string userName = rvtApp.Username;
                    bool dws = doc.IsWorkshared;
                    string viewName = "{3D}";
                    if (dws)
                    {
                        viewName = "{3D - " + userName + "}";

                    }

                    bool isActiveView3d = false;
                    foreach (Autodesk.Revit.DB.View view in views)
                    {
                        if (view.Name == viewName) 
                        { 
                            try { uidoc.ActiveView = view; Logger.Log("3D-вид открыт", 2); isActiveView3d = true; } 
                            catch (Exception e) { Logger.Log($"Не удалось открыть вид {viewName} {e.Message}", 4); }
                            break; 
                        }
                    }

                    if (isActiveView3d)
                    {
                        using (Transaction t2 = new Transaction(doc))
                        {
                            t2.Start("Задания от ИОС. Подрезка вида");
                            //подрезка вида по отверстию
                            view3d = (View3D)uidoc.ActiveGraphicalView;
                            if (bb != null) try { view3d.SetSectionBox(bb); } catch (Exception ex) { Logger.Log($"Не удалось подрезать вид: {ex.Message}", 4); }
                            t2.Commit();
                            Logger.Log("Вид подрезан", 2);
                        }
                    }
                    

                    // Добавляем ID скопированного отверстия для выделения
                    foreach (ElementId newElemId in newElemIds)
                    {
                        gIds += newElemId.ToString() + ",";
                    }

                    if (gIds.Length > 0)
                    {
                        gIds = gIds.Substring(0, gIds.Length - 1);
                        Logger.Log("Идентификаторы для выделения: " + gIds, 2);
                        RevitAPI.UiDocument.Selection.SetElementIds(gIds.Split(',').Select(s => new ElementId(int.Parse(s))).ToArray()); //выделение 
                        Logger.Log("Элементы выделены", 2);
                    }
                }
            }
        }
        public static void DeleteTaskElement(in Document doc, in string Id)
        {
            int idint = 1;
            bool isnameId = int.TryParse(Id, out idint);
            if (isnameId)
            {
                ElementId elemId = new ElementId(idint);
                Element elem = doc.GetElement(elemId);

                using (Transaction t = new Transaction(doc))
                {
                    t.Start("Задания от ИОС. Удаление лишнего элемента");
                    Logger.Log("Открываем транзакцию (удаление элемента)", 1);

                    // Получаем ID группы элемента
                    ElementId groupId = elem.GroupId;



                    if (groupId != null && groupId != ElementId.InvalidElementId)
                    {
                        Group targetGroupInCurrentModel = doc.GetElement(groupId) as Group;
                        try
                        {
                            // Проверяем, сколько групп с таким GroupType существует
                            GroupType groupType = targetGroupInCurrentModel.GroupType;
                            var groupsWithSameType = new FilteredElementCollector(doc)
                                .OfClass(typeof(Group))
                                .WhereElementIsNotElementType()
                                .Cast<Group>()
                                .Where(g => g.GroupType.Id == groupType.Id)
                                .ToList();

                            // Если групп больше одной - ошибка
                            if (groupsWithSameType.Count > 1)
                            {
                                Logger.Log($"Найдено {groupsWithSameType.Count} групп с типом {groupType.Name}. Должна быть только одна.", 4);
                                new InfoWindow280($"Ошибка!\nНайдено несколько групп с типом '{groupType.Name}'.\nВ модели должна быть только одна группа с таким типом.").ShowDialog();
                                throw new Exception("Найдено несколько групп с одинаковым типом");
                            }

                            // Получаем полное имя группы (с номером, если есть)
                            string fullGroupName = targetGroupInCurrentModel.Name;

                            // Разгруппируем группу
                            IList<ElementId> ungroupedElementIds = targetGroupInCurrentModel.UngroupMembers().ToList();
                            List<ElementId> newGroupElementIds = new List<ElementId>();
                            foreach (ElementId eId in ungroupedElementIds)
                            {
                                if (eId.IntegerValue != elem.Id.IntegerValue) newGroupElementIds.Add(eId);
                            }
                            Logger.Log("Группа разгруппирована", 2);

                            // Удаляем все GroupType с таким именем из модели
                            // Собираем все GroupType с таким именем
                            var allGroupTypes = new FilteredElementCollector(doc)
                                .OfClass(typeof(GroupType))
                                .Cast<GroupType>()
                                .Where(gt => gt.Name == fullGroupName)
                                .ToList();

                            // Удаляем найденные GroupType
                            List<ElementId> groupTypesToDelete = new List<ElementId>();
                            foreach (var gt in allGroupTypes)
                            {
                                groupTypesToDelete.Add(gt.Id);
                            }

                            if (groupTypesToDelete.Count > 0)
                            {
                                doc.Delete(groupTypesToDelete);
                            }
                            Logger.Log("Группы в Диспетчере проекта подчищены", 2);

                            // Создаем список элементов для новой группы
                            List<ElementId> elementsForNewGroup = new List<ElementId>(newGroupElementIds);

                            // Создаем новую группу
                            Group newGroup = doc.Create.NewGroup(elementsForNewGroup);
                            Logger.Log("Создаем новую группу", 2);

                            // Обновляем ссылку на группу
                            targetGroupInCurrentModel = doc.GetElement(newGroup.Id) as Group;

                            // Устанавливаем имя новой группы

                            // Находим все GroupType в проекте
                            FilteredElementCollector collector = new FilteredElementCollector(doc);
                            ICollection<Element> groupTypes = collector
                                .OfClass(typeof(GroupType))
                                .ToElements();
                            foreach (GroupType groupType1 in groupTypes)
                            {
                                if (groupType1.Name == newGroup.Name)
                                {
                                    groupType1.Name = fullGroupName; Logger.Log("Имя группы изменено", 2);
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log("Ошибка при удалении из группы: " + ex.Message, 4);
                        }
                    }

                    //Удаление самого элемента
                    ICollection<ElementId> elemstoremove = new List<ElementId>() { elem.Id };
                    doc.Delete(elemstoremove.ToArray());

                    Logger.Log("Элемент удален из группы", 1);

                    t.Commit(); Logger.Log("Закрываем транзакцию", 1);
                    //new InfoWindow280("Успешно!").ShowDialog();
                }



            }
        }
        public static void UpdateTaskElement(in Document doc, in string Input)
        {
            Guid adskHoleWidthParamGuid = new Guid("096bc30e-3c95-4637-84d5-9f6bf45d8676");//ADSK_Отверстие_Ширина
            Guid adskHoleHeightParamGuid = new Guid("bc4e92d8-db66-4e93-8923-3af6e2dc8599");//ADSK_Отверстие_Высота
            Guid adskDiamParamGuid = new Guid("9b679ab7-ea2e-49ce-90ab-0549d5aa36ff");//ADSK_Размер_Диаметр
            Guid adskWidthParamGuid = new Guid("8f2e4f93-9472-4941-a65d-0ac468fd6a5d");//ADSK_Размер_Ширина
            Guid adskHeightParamGuid = new Guid("da753fe3-ecfa-465b-9a2c-02f55d0c2ff1");//ADSK_Размер_Высота
            Guid adskLengthParamGuid = new Guid("748a2515-4cc9-4b74-9a69-339a8d65a212");//ADSK_Размер_Длина

            string[] names = Input.Split('=');
            string mark = names[0]; Logger.Log("Марка: " + mark, 2);
            string id0 = names[1]; Logger.Log("Марка: " + id0, 2);

            //"плохое" отверстие
            int idint = 1;
            bool isnameId = int.TryParse(id0, out idint);
            ElementId elemId = new ElementId(idint);
            Element elem0 = doc.GetElement(elemId);

            //"хорошее" отверстие
            BuiltInParameter gm = BuiltInParameter.ALL_MODEL_MODEL; //параметр Группа модели
            BuiltInParameter mrk = BuiltInParameter.ALL_MODEL_MARK; //Марка
            ElementId familyNameParamId = new ElementId(-1002002); //id параметра Имя семейства

            List<RevitLinkInstance> links = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RvtLinks)
                                                                        .WhereElementIsNotElementType()
                                                                        .Cast<RevitLinkInstance>()
                                                                        .ToList();

            List<RevitLinkInstance> taskLinks = new List<RevitLinkInstance>(); //пустой список изменяемых связей

            foreach (var link in links)
            {
                if (link.Name.Contains("Задани") || link.Name.Contains("задани") || link.Name.Contains("-ЗД") || link.Name.Contains("_ЗД") || link.Name.Contains("ЗАДАНИЕ")) taskLinks.Add(link);
            }

            if (taskLinks.Count > 1)
            {
                Logger.Log("Слишком много моделей заданий. Завершение работы.", 3);
                new InfoWindow280("Ошибка!\nСвязь задания вставлена больше одного раза, либо вставлено несколько разных связей заданий.\nОставьте только одну связь.").ShowDialog();

            }
            else if (taskLinks.Count == 1)
            {
                Document linkDoc = taskLinks[0].GetLinkDocument();
                var transform = taskLinks[0].GetTransform();

                List<Group> linkGroups = new FilteredElementCollector(linkDoc).OfCategory(BuiltInCategory.OST_IOSModelGroups)
                    .WhereElementIsNotElementType()
                    .Cast<Group>()
                    .ToList();

                List<FamilyInstance> GMs = new FilteredElementCollector(linkDoc).OfCategory(BuiltInCategory.OST_GenericModel)   //фильтр по категории Об модели
                                                                         .WhereElementIsNotElementType()
                                                                         .OfClass(typeof(FamilyInstance))
                                                                         .Cast<FamilyInstance>()
                                                                         .ToList();
                List<FamilyInstance> holesGM = new List<FamilyInstance>();

                List<Group> groups = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_IOSModelGroups)
                            .WhereElementIsNotElementType()
                            .Cast<Group>()
                            .ToList();

                foreach (FamilyInstance GM in GMs)
                {
                    string gmvalue = GM.Symbol.get_Parameter(gm).AsString();
                    if (gmvalue != null)
                    {
                        if (gmvalue.Contains("Отверстие")) holesGM.Add(GM);
                        else if (gmvalue.Contains("Рама")) holesGM.Add(GM);
                    }
                }
                ElementId elementId = new ElementId(0);
                foreach (FamilyInstance hole in holesGM)
                {
                    Element elem = (Element)hole;
                    string elem_mark = elem.get_Parameter(mrk).AsValueString();
                    if (elem_mark == mark)
                    {
                        elementId = elem.Id; break;
                    }
                }

                if (elementId.IntegerValue != 0)
                {
                    //открытие 3д-вида
                    Autodesk.Revit.DB.View3D view3d;

                    List<Autodesk.Revit.DB.View> views = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Views)   //фильтр по категории Виды
                                                                                    .WhereElementIsNotElementType()    //фильтр только экземпляры
                                                                                    .Cast<Autodesk.Revit.DB.View>()                     //элементы категории Виды
                                                                                    .ToList();                         //формируем список
                    UIDocument uidoc = RevitAPI.UiDocument;
                    UIApplication uiApp = RevitAPI.UiApplication; Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
                    string userName = rvtApp.Username;
                    bool dws = doc.IsWorkshared;
                    string viewName = "{3D}";
                    if (dws)
                    {
                        viewName = "{3D - " + userName + "}";

                    }
                    

                    string gIds = "";


                    using (Transaction t = new Transaction(doc))
                    {
                        t.Start("Задания от ИОС. Изменение элемента"); Logger.Log("Открываем транзакцию (изменение элемента)", 1);

                        // Получаем ID группы элемента
                        ElementId groupId = elem0.GroupId;



                        if (groupId != null && groupId != ElementId.InvalidElementId)
                        {
                            Group targetGroupInCurrentModel = doc.GetElement(groupId) as Group;
                            try
                            {
                                // Проверяем, сколько групп с таким GroupType существует
                                GroupType groupType = targetGroupInCurrentModel.GroupType;
                                var groupsWithSameType = new FilteredElementCollector(doc)
                                    .OfClass(typeof(Group))
                                    .WhereElementIsNotElementType()
                                    .Cast<Group>()
                                    .Where(g => g.GroupType.Id == groupType.Id)
                                    .ToList();

                                // Если групп больше одной - ошибка
                                if (groupsWithSameType.Count > 1)
                                {
                                    Logger.Log($"Найдено {groupsWithSameType.Count} групп с типом {groupType.Name}. Должна быть только одна.", 4);
                                    new InfoWindow280($"Ошибка!\nНайдено несколько групп с типом '{groupType.Name}'.\nВ модели должна быть только одна группа с таким типом.").ShowDialog();
                                    throw new Exception("Найдено несколько групп с одинаковым типом");
                                }

                                // Получаем полное имя группы (с номером, если есть)
                                string fullGroupName = targetGroupInCurrentModel.Name;

                                // Разгруппируем группу
                                IList<ElementId> ungroupedElementIds = targetGroupInCurrentModel.UngroupMembers().ToList();
                                Logger.Log("Группа разгруппирована", 2);

                                // Шаг 2: Удаляем все GroupType с таким именем из модели
                                // Собираем все GroupType с таким именем
                                var allGroupTypes = new FilteredElementCollector(doc)
                                    .OfClass(typeof(GroupType))
                                    .Cast<GroupType>()
                                    .Where(gt => gt.Name == fullGroupName)
                                    .ToList();

                                // Удаляем найденные GroupType
                                List<ElementId> groupTypesToDelete = new List<ElementId>();
                                foreach (var gt in allGroupTypes)
                                {
                                    groupTypesToDelete.Add(gt.Id);
                                }

                                if (groupTypesToDelete.Count > 0)
                                {
                                    doc.Delete(groupTypesToDelete);
                                }
                                Logger.Log("Группы в Диспетчере проекта подчищены", 2);

                                // изменяем параметры элемента либо заменяем его

                                int d = 0;
                                Element elem = linkDoc.GetElement(elementId);

                                bool elemIsHole = true;
                                FamilyInstance familyInstance = (FamilyInstance)elem;
                                string gmvalue = familyInstance.Symbol.get_Parameter(gm).AsString();
                                if (gmvalue != null)
                                {
                                    if (gmvalue.Contains("Рама под оборудование")) elemIsHole=false;
                                }

                                Guid widthParam = adskHoleWidthParamGuid; Guid heightParam = adskHoleHeightParamGuid;
                                Guid lengthParam = adskHoleWidthParamGuid;

                                if (!elemIsHole)
                                {
                                    widthParam = adskWidthParamGuid; heightParam = adskHeightParamGuid;
                                    lengthParam = adskLengthParamGuid;
                                }


                                string widthParamName = "Ширина: "; string heightParamName = "Высота: ";
                                foreach (Parameter param in elem.ParametersMap) //"хорошее" - круглое?
                                {
                                    if (param.IsShared)
                                    {
                                        Guid paramGUID = param.GUID;
                                        if (paramGUID == adskDiamParamGuid)
                                        {
                                            d++; Logger.Log("Отверстие в связанной модели - круглое", 2);
                                            widthParamName = "Диаметр: "; heightParamName = "Диаметр: ";
                                            break;
                                        }
                                    }
                                }
                                int d0 = 0;
                                foreach (Parameter param in elem0.ParametersMap) //"плохое" - круглое?
                                {
                                    if (param.IsShared)
                                    {
                                        Guid paramGUID = param.GUID;
                                        if (paramGUID == adskDiamParamGuid)
                                        {
                                            d0++; Logger.Log("Отверстие в текущей модели - круглое", 2);
                                            break;
                                        }
                                    }
                                }

                                // Создаем список элементов для новой группы
                                List<ElementId> elementsForNewGroup = new List<ElementId>(ungroupedElementIds);

                                if (d != d0) //в одной модели круглое, в другой - прямоугольное: ЗАМЕНА ОТВЕРСТИЯ
                                {
                                    Logger.Log("Замена элемента", 1);
                                    //Удаление элемента в текущей модели
                                    ICollection<ElementId> elemstoremove = new List<ElementId>() { elem0.Id };
                                    doc.Delete(elemstoremove.ToArray());
                                    Logger.Log("   Удален старый элемент", 2);
                                    //Вставка нового элемента
                                    ICollection<ElementId> ids = new HashSet<ElementId>();
                                    ids.Add(elementId);
                                    CopyPasteOptions copyOptions2 = new CopyPasteOptions();
                                    ICollection<ElementId> newElemIds = new List<ElementId>();
                                    newElemIds = ElementTransformUtils.CopyElements(linkDoc, ids, doc, transform, copyOptions2);
                                    //новая коллекция
                                    elementsForNewGroup = new List<ElementId>(ungroupedElementIds.Where(id => id != elem0.Id)
                                            .Concat(newElemIds).ToList());
                                    Logger.Log("Элемент заменен", 1);
                                }
                                else //изменение параметров БЕЗ ЗАМЕНЫ (ВСЕ ВИДЫ ЗАДАНИЙ)
                                {
                                    Logger.Log("Изменение параметров элемента", 1);

                                    Logger.Log("Элемент в связанной модели:", 2);
                                    //новое
                                    double elem_width = elem.get_Parameter(widthParam).AsDouble();
                                    Logger.Log("   " + widthParamName + elem_width.ToString(), 2);
                                    double elem_height = elem.get_Parameter(heightParam).AsDouble();
                                    Logger.Log("   " + heightParamName + elem_height.ToString(), 2);
                                    double elem_length = elem.get_Parameter(lengthParam).AsDouble();
                                    Logger.Log("   Длина: " + elem_length.ToString(), 2);
                                    LocationPoint elem_lp = (LocationPoint)elem.Location;
                                    XYZ point = elem_lp.Point; point = transform.OfPoint(point);
                                    double elem_x = point.X; double elem_y = point.Y; double elem_z = point.Z;
                                    Logger.Log("   x: " + elem_x.ToString()+ " y: " + elem_y.ToString()+ " z: " + elem_z.ToString(), 2);
                                    double elem_rotation = elem_lp.Rotation;
                                    Logger.Log("   rotation: " + elem_lp.Rotation.ToString(), 2);

                                    Logger.Log("Текущий элемент:", 2);
                                    //старое
                                    double elem0_width = elem0.get_Parameter(widthParam).AsDouble();
                                    Logger.Log("   " + widthParamName + elem0_width.ToString(), 2);
                                    double elem0_height = elem0.get_Parameter(heightParam).AsDouble();
                                    Logger.Log("   " + heightParamName + elem0_height.ToString(), 2);
                                    double elem0_length = elem0.get_Parameter(lengthParam).AsDouble();
                                    Logger.Log("   Длина: " + elem0_length.ToString(), 2);
                                    LocationPoint elem0_lp = (LocationPoint)elem0.Location;
                                    XYZ point0 = elem0_lp.Point;
                                    double elem0_x = point0.X; double elem0_y = point0.Y; double elem0_z = point0.Z;
                                    Logger.Log("   x: " + elem0_x.ToString() + " y: " + elem0_y.ToString() + " z: " + elem0_z.ToString(), 2);
                                    double elem0_rotation = elem0_lp.Rotation;
                                    Logger.Log("   rotation: " + elem0_lp.Rotation.ToString(), 2);

                                    //сравнение и замена параметров
                                    if (elem0_width != elem_width)
                                    {
                                        elem0.get_Parameter(widthParam).Set(elem_width);
                                        Logger.Log(widthParam + ": назначено " + elem_width.ToString(), 2);
                                    }
                                    if (elem0_height != elem_height) 
                                    { 
                                        elem0.get_Parameter(heightParam).Set(elem_height); 
                                        Logger.Log(heightParam + ": назначено " + elem_height.ToString(), 2); 
                                    }
                                    if (!elemIsHole && elem0_length != elem_length)
                                    {
                                        elem0.get_Parameter(lengthParam).Set(elem_length);
                                        Logger.Log("Длина: назначено " + elem_length.ToString(), 2);
                                    }
                                    if (elem0_x != elem_x || elem0_y != elem_y || elem0_z != elem_z)
                                    {
                                        XYZ translation = point - point0;
                                        ElementTransformUtils.MoveElement(doc, elem0.Id, translation);
                                        Logger.Log("Элемент перемещен", 2);
                                    }
                                    if (elem0_rotation != elem_rotation)
                                    {
                                        double rotationDifference = elem_rotation - elem0_rotation;
                                        if (Math.Abs(rotationDifference) > 0.001)
                                        {
                                            Line axis = Line.CreateBound(point, new XYZ(point.X, point.Y, point.Z + 10));
                                            ElementTransformUtils.RotateElement(doc, elem0.Id, axis, rotationDifference);
                                            Logger.Log("Элемент повернут", 2);
                                        }
                                    }
                                }

                                // Создаем новую группу
                                Group newGroup = doc.Create.NewGroup(elementsForNewGroup);

                                // Обновляем ссылку на группу
                                targetGroupInCurrentModel = doc.GetElement(newGroup.Id) as Group;

                                // Устанавливаем имя новой группы

                                // Находим все GroupType в проекте
                                FilteredElementCollector collector = new FilteredElementCollector(doc);
                                ICollection<Element> groupTypes = collector
                                    .OfClass(typeof(GroupType))
                                    .ToElements();
                                foreach (GroupType groupType1 in groupTypes)
                                {
                                    if (groupType1.Name == newGroup.Name)
                                    {
                                        groupType1.Name = fullGroupName; Logger.Log("Имя группы изменено", 1);
                                        break;
                                    }
                                }
                                Logger.Log("Элемент изменен в группе: " + fullGroupName, 1);
                                //new InfoWindow280("Успешно!").ShowDialog();
                            }
                            catch (Exception ex)
                            {
                                Logger.Log("Ошибка при изменении в группе: " + ex.Message, 4);
                            }
                        }

                        t.Commit(); Logger.Log("Закрываем транзакцию", 1);

                        //new InfoWindow280("Успешно!").ShowDialog();
                    }




                    bool isActiveView3d = false;
                    foreach (Autodesk.Revit.DB.View view in views)
                    {
                        if (view.Name == viewName)
                        {
                            try { uidoc.ActiveView = view; Logger.Log("3D-вид открыт", 2); isActiveView3d = true; }
                            catch (Exception e) { Logger.Log($"Не удалось открыть вид {viewName} {e.Message}", 4); }
                            break;
                        }
                    }

                    BoundingBoxXYZ el0_box = elem0.get_BoundingBox(doc.ActiveView);
                    if (isActiveView3d)
                    {
                        using (Transaction t2 = new Transaction(doc))
                        {
                            t2.Start("Задания от ИОС. Подрезка вида");
                            //подрезка вида по отверстию
                            view3d = (View3D)uidoc.ActiveGraphicalView;
                            if (el0_box != null) try { view3d.SetSectionBox(el0_box); } catch (Exception ex) { Logger.Log($"Не удалось подрезать вид: {ex.Message}", 4); }
                            t2.Commit();
                            Logger.Log("Вид подрезан", 2);
                        }
                    }

                    
                    
                    //выделение отверстия в модели
                    gIds += elemId; Logger.Log("Элементы: " + gIds, 2);
                    RevitAPI.UiDocument.Selection.SetElementIds(gIds.Split(',').Select(s => new ElementId(int.Parse(s))).ToArray()); //выделение 
                    Logger.Log("Элементы выделены", 2);


                }




            }
        }
        public static void ReplaceTaskGroup(in Document doc, in string Name, in DateTime dateTime)
        {
            string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            QuestionWindowViewModel qViewModel1 = new QuestionWindowViewModel();
            qViewModel1.headtxt = "Внимание! При перевставке группы с отверстиями удаляются все размеры и марки, " +
                "привязанные к отверстиям этой группы. Продолжить?";
            var qwpfview1 = new QuestionWindow280(qViewModel1);
            qViewModel1.CloseRequest += (s, e) => qwpfview1.Close();
            bool? qok1 = qwpfview1.ShowDialog();
            if (qok1 != null && qok1 == true) { }
            else
            {
                Logger.Log("Отменено. Завершение работы", 3); return;
            }

            List<RevitLinkInstance> links = new FilteredElementCollector(RevitAPI.Document).OfCategory(BuiltInCategory.OST_RvtLinks)
                                                                         .WhereElementIsNotElementType()
                                                                         .Cast<RevitLinkInstance>()
                                                                         .ToList();

            List<RevitLinkInstance> taskLinks = new List<RevitLinkInstance>(); //пустой список изменяемых связей

            foreach (var link in links)
            {
                if (link.Name.Contains("Задани") || link.Name.Contains("задани") || link.Name.Contains("-ЗД") || link.Name.Contains("_ЗД") || link.Name.Contains("ЗАДАНИЕ")) taskLinks.Add(link);
            }

            if (taskLinks.Count > 1)
            {
                Logger.Log("Слишком много моделей заданий. Завершение работы.", 3);
                new InfoWindow280("Ошибка!\nСвязь задания вставлена больше одного раза, либо вставлено несколько разных связей заданий.\nОставьте только одну связь.").ShowDialog();
                return;
            }

            //удаление
            List<Group> groups = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_IOSModelGroups)
                .WhereElementIsNotElementType()
                .Cast<Group>()
                .ToList();
            ICollection<ElementId> elemstoremove = new List<ElementId>();
            string gName = "";
            foreach (var group in groups)
            {
                string[] nameParts1 = group.Name.Split('_');
                string shortName1 = group.Name;
                if (nameParts1.Length > 2) shortName1 = nameParts1[0] + '_' + nameParts1[1] + '_' + nameParts1[2];
                if (shortName1 == Name)
                {
                    elemstoremove.Add(group.Id); gName = group.Name;
                    //break;не надо - удаляем все такие группы!
                }
            }
            using (Transaction t = new Transaction(doc))
            {
                t.Start("Задания от ИОС. Удаление группы");
                Logger.Log("Выбран сценарий перевставки группы. Завершение работы", 5);
                Logger.Initialize("Задания Перевставка",dateTime,TNovVersion);
                if (elemstoremove.Count > 0) doc.Delete(elemstoremove.ToArray());
                t.Commit();
            }
            Logger.Log($"Группа {gName} удалена", 1);

            // группы в связанной модели задания

            Document linkDoc = taskLinks[0].GetLinkDocument(); var transform = taskLinks[0].GetTransform();
            List<Group> linkGroups = new FilteredElementCollector(linkDoc).OfCategory(BuiltInCategory.OST_IOSModelGroups)
                .WhereElementIsNotElementType()
                .Cast<Group>()
                .ToList();
            ICollection<ElementId> ids = new HashSet<ElementId>();
            foreach (var linkGroup in linkGroups)
            {
                string[] nameParts = linkGroup.Name.Split('_');
                string shortName = linkGroup.Name;
                if (nameParts.Length > 2) shortName = nameParts[0] + '_' + nameParts[1] + '_' + nameParts[2];
                if (shortName == Name)
                {
                    ids.Add(linkGroup.Id);
                    break;
                }
            }
            CopyPasteOptions copyOptions = new CopyPasteOptions();
            using (Transaction t2 = new Transaction(doc))
            {
                t2.Start("Задания от ИОС. Вставка группы");
                ElementTransformUtils.CopyElements(linkDoc, ids, doc, transform, copyOptions);
                t2.Commit();
            }
            Logger.Log($"Группа {gName} вставлена. Завершение работы", 5);
            Logger.Initialize("Задание получить",dateTime,TNovVersion);
        }
    }
}
