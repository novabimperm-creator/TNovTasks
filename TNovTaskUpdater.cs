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
        static AddInId _appId;
        static UpdaterId _updaterId;

        public TNovTaskUpdater(AddInId id)
        {
            _appId = id;

            _updaterId = new UpdaterId(_appId, new Guid(
                                                   "9d5b2399-c4a4-457b-9306-63a64aca0c02"));
        }

        public void Execute(UpdaterData data)
        {
            Document doc = data.GetDocument();
            Autodesk.Revit.ApplicationServices.Application app = doc.Application;

            //параметры
            ElementId familyNameParamId = new ElementId(-1002002); //id параметра Имя семейства
            Guid adskGparamGuid = new Guid("3de5f1a4-d560-4fa8-a74f-25d250fb3401");//ADSK_Группирование

            //проверка имени файла
            string docName = doc.Title.ToString();
            bool taskModel = false; if (docName.Contains("Задани") || docName.Contains("задани") || docName.Contains("-ЗД") || docName.Contains("_ЗД") || docName.Contains("ЗАДАНИЕ")) taskModel = true;

            if (taskModel) 
            {
                //проверка подключения к серверу
                string usagefilePath = nova.novaserver + "_TNov/usage.txt";
                bool servercheck = File.Exists(usagefilePath);

                if (servercheck)
                {
                    List<ElementId> idsA = data.GetAddedElementIds().ToList();
                    List<ElementId> idsM = data.GetModifiedElementIds().ToList();
                    List<ElementId> ids = new List<ElementId>();

                    
                    foreach (var id in idsA)
                    {
                        Element elem = doc.GetElement(id);
                        ids.Add(id);
                    }
                    foreach (var id in idsM)
                    {
                        Element elem = doc.GetElement(id);
                        ids.Add(id);
                    }

                    foreach (ElementId id in ids)
                    {
                        Element elem = doc.GetElement(id);
                        if (null != elem)
                        {
                            try
                            {
                                string adskGvalue = "";
                                if (elem.Name.Contains("КЖ"))
                                {
                                    if (elem.Name.Contains("Стены") || elem.Name.Contains("стены")) adskGvalue = "КЖ.Стены";
                                    else if (elem.Name.Contains("Плиты") || elem.Name.Contains("плиты")) adskGvalue = "КЖ.Плиты";
                                }
                                else if (elem.Name.Contains("КР"))
                                {
                                    if (elem.Name.Contains("Стены") || elem.Name.Contains("стены")) adskGvalue = "КР.Стены";
                                }
                                if (elem.Name.Contains("Шахты")) adskGvalue = "КР.Шахты";
                                if (elem.Name.Contains("Рамы")) adskGvalue = "КР.Рамы";
                                if (elem.Name.Contains("Приямки")) adskGvalue = "КЖ.Приямки";

                                if (adskGvalue.Length > 0 && elem.get_Parameter(adskGparamGuid).IsReadOnly != true)
                                {
                                    elem.get_Parameter(adskGparamGuid)?.Set(adskGvalue);
                                }
                                /*
                                string[] nameParts = elem.Name.Split('_');
                                string shortName = elem.Name;
                                if (nameParts.Length < 3)
                                { 
                                    new InfoWindow280("Некорректное имя группы " + elem.Name + " - необходимо наличие блоков ОтКого_Кому_Этаж.").ShowDialog();
                                    string commandText = @"https://portal.talan.elem/knowledge/proektirovanie/MEPtasks/";
                                    var proc = new System.Diagnostics.Process();
                                    proc.StartInfo.FileName = commandText;
                                    proc.StartInfo.UseShellExecute = true;
                                    proc.Start();
                                }
                                */
                            }
                            catch (Exception) { }

                        }

                    }
                    
                }
            }

            
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
            return "TNovTaskUpdater";
        }
    }
}
