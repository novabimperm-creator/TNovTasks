using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.Attributes;
using TNovCommon;

namespace TNovTasks
{
    [Transaction(TransactionMode.Manual)]
    public class TNovTaskUpdater : IUpdater
    {
        private const string UpdaterName = "TNovTaskUpdater";

        static AddInId _appId;
        static UpdaterId _updaterId;

        public TNovTaskUpdater(AddInId id)
        {
            _appId = id;

            _updaterId = new UpdaterId(_appId, new Guid(
                                                   "9d5b2399-c4a4-457b-9306-63a64aca0c02"));
        }

        /// <summary>
        /// Точка входа Revit. Наружу не должно вылетать ни одного исключения:
        /// любое исключение из IUpdater.Execute Revit показывает пользователю
        /// с предложением отключить обновитель.
        /// </summary>
        public void Execute(UpdaterData data)
        {
            try
            {
                ExecuteCore(data);
            }
            catch (Exception ex)
            {
                UpdaterDiagnostics.Report(UpdaterName, "Execute", ex);
            }
        }

        private void ExecuteCore(UpdaterData data)
        {
            if (data == null) return;

            Document doc = data.GetDocument();
            if (doc == null || doc.IsFamilyDocument) return;

            //параметры
            Guid adskGparamGuid = new Guid("3de5f1a4-d560-4fa8-a74f-25d250fb3401");//ADSK_Группирование

            //проверка имени файла
            string docName = doc.Title ?? "";
            bool taskModel = docName.Contains("Задани") || docName.Contains("задани")
                || docName.Contains("-ЗД") || docName.Contains("_ЗД") || docName.Contains("ЗАДАНИЕ");
            if (!taskModel) return;

            var ids = new HashSet<ElementId>();
            ICollection<ElementId> idsA = data.GetAddedElementIds();
            if (idsA != null) ids.UnionWith(idsA);
            ICollection<ElementId> idsM = data.GetModifiedElementIds();
            if (idsM != null) ids.UnionWith(idsM);

            foreach (ElementId id in ids)
            {
                // Сбой на одном элементе не должен ронять обработку остальных
                try
                {
                    Element elem = doc.GetElement(id);
                    if (null == elem) continue;

                    string name = ElementName(elem);
                    if (name.Length == 0) continue;

                    string adskGvalue = "";
                    if (name.Contains("КЖ"))
                    {
                        if (name.Contains("Стены") || name.Contains("стены")) adskGvalue = "КЖ.Стены";
                        else if (name.Contains("Плиты") || name.Contains("плиты")) adskGvalue = "КЖ.Плиты";
                    }
                    else if (name.Contains("КР"))
                    {
                        if (name.Contains("Стены") || name.Contains("стены")) adskGvalue = "КР.Стены";
                    }
                    if (name.Contains("Шахты")) adskGvalue = "КР.Шахты";
                    if (name.Contains("Рамы")) adskGvalue = "КР.Рамы";
                    if (name.Contains("Приямки")) adskGvalue = "КЖ.Приямки";

                    if (adskGvalue.Length > 0) //ADSK_Группирование
                        UpdaterUtils.TrySetString(UpdaterUtils.GetWritableParam(elem, adskGparamGuid), adskGvalue);
                }
                catch (Exception ex)
                {
                    UpdaterDiagnostics.Report(UpdaterName, "элемент " + UpdaterUtils.IdText(id), ex);
                }
            }
        }

        private static string ElementName(Element elem)
        {
            try { return elem.Name ?? ""; }
            catch { return ""; }
        }

        public string GetAdditionalInformation()
        {
            return "TNov, bim@pm-nova.ru";
        }

        public ChangePriority GetChangePriority()
        {
            return ChangePriority.FloorsRoofsStructuralWalls;
        }

        public UpdaterId GetUpdaterId()
        {
            return _updaterId;
        }

        public string GetUpdaterName()
        {
            return UpdaterName;
        }
    }
}
