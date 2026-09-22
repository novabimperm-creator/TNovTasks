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
    public class TNovShaftUpdater : IUpdater
    {
        private const string UpdaterName = "TNovShaftUpdater";

        static AddInId _appId;
        static UpdaterId _updaterId;

        public TNovShaftUpdater(AddInId id)
        {
            _appId = id;

            _updaterId = new UpdaterId(_appId, new Guid(
                                                   "d1915654-7bc0-4643-a03f-4165ef111810"));
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
            Autodesk.Revit.ApplicationServices.Application app = doc.Application;

            //параметры
            ElementId familyNameParamId = new ElementId(-1002002); //id параметра Имя семейства
            Guid adskGparamGuid = new Guid("3de5f1a4-d560-4fa8-a74f-25d250fb3401");//ADSK_Группирование
            Guid NTNovTextparamGuid = new Guid("b00446ce-acf8-498e-add9-a3603abe9028"); //N_TNov_Text
            Guid NTaskApprovedBIMParamGuid = new Guid("94587b6e-5bdd-4fe8-bea4-4996c32801c4");//N_Согласовано BIM
            Guid NTaskApprovedSTParamGuid = new Guid("7cb33aa5-8106-4e4c-8038-6691e34f438c");//N_Согласовано КР
            //04.2026: параметр N_Согласовано рук исключен как устаревший, для совместимости заполняется как "1"
            Guid adskWidthParamGuid = new Guid("8f2e4f93-9472-4941-a65d-0ac468fd6a5d");//ADSK_Размер_Ширина
            Guid adskHeightParamGuid = new Guid("da753fe3-ecfa-465b-9a2c-02f55d0c2ff1");//ADSK_Размер_Высота
            Guid adskLengthParamGuid = new Guid("748a2515-4cc9-4b74-9a69-339a8d65a212");//ADSK_Размер_Длина

            //проверка имени файла
            string docName = doc.Title ?? "";
            bool taskModel = false; if (docName.Contains("Задани") || docName.Contains("задани") || docName.Contains("-ЗД") || docName.Contains("_ЗД") || docName.Contains("ЗАДАНИЕ")) taskModel = true;

            if (taskModel)
            {

                    var allIds = new HashSet<ElementId>();
                    ICollection<ElementId> idsA = data.GetAddedElementIds();
                    if (idsA != null) allIds.UnionWith(idsA);
                    ICollection<ElementId> idsM = data.GetModifiedElementIds();
                    if (idsM != null) allIds.UnionWith(idsM);
                    if (allIds.Count == 0) return;

                    List<ElementId> ids = new List<ElementId>();

                    ElementFilter elementFilter = (ElementFilter)new ElementParameterFilter(RevitApiCompat.CreateContainsRule(familyNameParamId, "pmN.Рама под оборудование"));
                    ElementFilter elementFilter2 = (ElementFilter)new ElementParameterFilter(RevitApiCompat.CreateContainsRule(familyNameParamId, "pmN.Задание на шахту"));
                    ElementFilter elementFilter3 = (ElementFilter)new ElementParameterFilter(RevitApiCompat.CreateContainsRule(familyNameParamId, "pmN.Задание на приямок"));

                    foreach (var id in allIds)
                    {
                        try
                        {
                            Element elem = doc.GetElement(id);
                            if (elem == null) continue;
                            if (elementFilter.PassesFilter(elem)) ids.Add(id);
                            else if (elementFilter2.PassesFilter(elem)) ids.Add(id);
                            else if (elementFilter3.PassesFilter(elem)) ids.Add(id);
                        }
                        catch (Exception ex)
                        {
                            UpdaterDiagnostics.Report(UpdaterName, "фильтр, элемент " + UpdaterUtils.IdText(id), ex);
                        }
                    }

                    


                    foreach (ElementId id in ids)
                    {
                        Element elem = doc.GetElement(id);
                        if (null != elem)
                        {
                            try
                            {
                            //заполнение группирования
                            if (RevitApiCompat.ElementIdIntValue(elem.GroupId) != -1) //отверстие - в группе
                            {
                                Element group = doc.GetElement(elem.GroupId);
                                if (group != null)
                                {
                                    string adskGvalue = "";
                                    if (group.Name.Contains("КЖ"))
                                    {
                                        if (group.Name.Contains("Стены") || group.Name.Contains("стены")) adskGvalue = "КЖ.Стены";
                                        else if (group.Name.Contains("Плиты") || group.Name.Contains("плиты")) adskGvalue = "КЖ.Плиты";
                                    }
                                    else if (group.Name.Contains("КР"))
                                    {
                                        if (group.Name.Contains("Стены") || group.Name.Contains("стены")) adskGvalue = "КР.Стены";
                                    }
                                    if (group.Name.Contains("Шахты")) adskGvalue = "КР.Шахты";
                                    if (group.Name.Contains("Рамы")) adskGvalue = "КР.Рамы";
                                    if (group.Name.Contains("Приямки")) adskGvalue = "КЖ.Приямки";

                                    if (adskGvalue.Length > 0)
                                    {
                                        elem.get_Parameter(adskGparamGuid)?.Set(adskGvalue);
                                        //group.get_Parameter(adskGparamGuid)?.Set(adskGvalue);
                                    }
                                }
                            }

                            //имя и роль пользователя (роль читается с сервера, поэтому кэшируется)
                            string userDepartment = TNovHoleUpdater.GetUserDepartment(app);

                            string prevValue = "0";
                            bool TNovTextParamExist = Param.ParamExistByGuid(NTNovTextparamGuid, elem);
                            if(TNovTextParamExist) { try { prevValue = elem.get_Parameter(NTNovTextparamGuid).AsValueString(); } catch (Exception) { } }
                            bool prevValues = true;
                            if(prevValue==null||prevValue=="0") prevValues = false;
                            if(prevValues&&elem.Location != null)
                            {
                                //считываем предыдущие значения параметров
                                ///структура значения параметра: СоглРук=СоглBIM=СоглКР=СуммаРазмеров=Координаты
                                string[] pars = prevValue.Split('=');
                                int Headstatus0 = 0; if (pars[0] == "1") Headstatus0 = 1;
                                int BIMstatus0 = 0; if (pars.Length > 1 && pars[1] == "1") BIMstatus0 = 1;
                                int STstatus0 = 0; if (pars.Length > 2 && pars[2] == "1") STstatus0 = 1;
                                double dims0 = 0; if (pars.Length > 3 && pars[3].Length > 0) Double.TryParse(pars[3], out dims0);
                                double point0 = 0; if(pars.Length > 4 && pars[4].Length>0) Double.TryParse(pars[4], out point0);
                                //считываем новые значения параметров
                                int BIMstatus = elem.get_Parameter(NTaskApprovedBIMParamGuid).AsInteger();
                                int STstatus = elem.get_Parameter(NTaskApprovedSTParamGuid).AsInteger();
                                double elem_width = elem.get_Parameter(adskWidthParamGuid).AsDouble(); double elem_height = elem.get_Parameter(adskHeightParamGuid).AsDouble();
                                double elem_length = elem.get_Parameter(adskLengthParamGuid).AsDouble();
                                double dims = elem_length * 0.3048 * 1000000000 + elem_width * 0.3048 * 1000000 + elem_height * 0.3048 * 1000; dims = Math.Round(dims);
                                LocationPoint elem_lp = (LocationPoint)elem.Location;
                                XYZ p = elem_lp.Point; double point = p.X * 0.3048 * 1000000000 + p.Y * 0.3048 * 1000000 + p.Z * 0.3048*1000; point=Math.Round(point);
                                //формируем новые значения параметров для записи
                                int BIMstatus1 = BIMstatus; int STstatus1 = STstatus;
                                //изменяем значения параметров при необходимости

                                int issues = 0;

                                
                                if(pars.Length > 1 && BIMstatus != BIMstatus0) //Согласовано BIM
                                {
                                    issues++; //MessageBox.Show("BIM");
                                    switch (userDepartment)
                                    {
                                        case "BIM":
                                            break;
                                        
                                        default:
                                            if (BIMstatus0 == 0) 
                                            { 
                                                BIMstatus1 = BIMstatus0; //галочка была неактивна - ее нельзя поставить
                                                elem.get_Parameter(NTaskApprovedBIMParamGuid).Set(BIMstatus1);
                                            }
                                            else
                                            {
                                                if (STstatus != STstatus0) break; //меняли только статус КР - не влияет на согласование BIM
                                                else
                                                {
                                                    BIMstatus1 = 0; //выключаем галочку при любых других изменениях
                                                    elem.get_Parameter(NTaskApprovedBIMParamGuid).Set(BIMstatus1);
                                                }
                                            }
                                            break;
                                    }
                                }
                                if (pars.Length > 2 && STstatus != STstatus0) //Согласовано КР
                                {
                                    issues++; //MessageBox.Show("КР");
                                    switch (userDepartment)
                                    {
                                        case "ST":
                                            break;
                                        case "BIM": //добавлено 01.2026
                                            break;
                                        case "AR": //добавлено 04.2026
                                            break;
                                        default:
                                            if (STstatus0 == 0) 
                                            { 
                                                STstatus1 = STstatus0;
                                                elem.get_Parameter(NTaskApprovedSTParamGuid).Set(STstatus1);
                                            }
                                            else
                                            {
                                                if (BIMstatus != BIMstatus0) break;
                                                else 
                                                { 
                                                    STstatus1 = 0; 
                                                    elem.get_Parameter(NTaskApprovedSTParamGuid).Set(STstatus1);
                                                }
                                            }
                                            break;
                                    }
                                }
                                
                                if(pars.Length > 3 && dims !=dims0)//сумма размеров
                                {
                                    //MessageBox.Show("размеры");
                                    switch (userDepartment)
                                    {
                                        case "BIM":
                                            /*if (STstatus == 1)
                                            {
                                                STstatus1 = 0; elem.get_Parameter(NTaskApprovedSTParamGuid).Set(STstatus1);
                                            }*/
                                            break; //изменено  01.2026

                                        case "ST":
                                            break;
                                        case "AR": //добавлено 04.2026
                                            break;

                                        default:
                                            if (STstatus == 1)
                                            {
                                                STstatus1 = 0; elem.get_Parameter(NTaskApprovedSTParamGuid).Set(STstatus1);
                                            }
                                            if (BIMstatus == 1)
                                            {
                                                BIMstatus1 = 0; elem.get_Parameter(NTaskApprovedBIMParamGuid).Set(BIMstatus1);
                                                
                                            }

                                            break;
                                    }
                                }
                                if (pars.Length > 4 && point != point0)//сумма координат
                                {
                                    //MessageBox.Show("координаты");
                                    switch (userDepartment)
                                    {
                                        case "BIM":
                                            /*if (STstatus == 1)
                                            {
                                                STstatus1 = 0; elem.get_Parameter(NTaskApprovedSTParamGuid).Set(STstatus1);
                                            }*/
                                            break; //изменено  01.2026

                                        case "ST":
                                            break;

                                        case "AR": //добавлено 04.2026
                                            break;

                                        default:
                                            if (STstatus == 1)
                                            {
                                                STstatus1 = 0; elem.get_Parameter(NTaskApprovedSTParamGuid).Set(STstatus1);
                                            }
                                            if (BIMstatus == 1)
                                            {
                                                BIMstatus1 = 0; elem.get_Parameter(NTaskApprovedBIMParamGuid).Set(BIMstatus1);

                                            }

                                            break;
                                    }
                                }
                                //записываем новые значения параметров
                                elem.get_Parameter(NTNovTextparamGuid).Set("1=" + BIMstatus1.ToString() + "=" + STstatus1.ToString() 
                                    + "=" + dims.ToString() + "=" +point.ToString());
                                
                                
                                
                                
                                
                            }
                            else
                            {
                                if (elem.Location != null)
                                {
                                    int BIMstatus = elem.get_Parameter(NTaskApprovedBIMParamGuid).AsInteger();
                                    int STstatus = elem.get_Parameter(NTaskApprovedSTParamGuid).AsInteger();
                                    double elem_width = elem.get_Parameter(adskWidthParamGuid).AsDouble(); double elem_height = elem.get_Parameter(adskHeightParamGuid).AsDouble();
                                    double elem_length = elem.get_Parameter(adskLengthParamGuid).AsDouble();
                                    double dims = elem_length * 0.3048 * 1000000000 + elem_width * 0.3048 * 1000000 + elem_height * 0.3048 * 1000; dims = Math.Round(dims);
                                    LocationPoint elem_lp = (LocationPoint)elem.Location;
                                    XYZ p = elem_lp.Point; double point = p.X * 0.3048 * 1000000000 + p.Y * 0.3048 * 1000000 + p.Z * 0.3048 * 1000; point = Math.Round(point);

                                    int BIMstatus1 = BIMstatus; int STstatus1 = STstatus;
                                    if (Param.ParamExistByGuid(NTNovTextparamGuid, elem))
                                    {
                                        //записываем новые значения параметров
                                        elem.get_Parameter(NTNovTextparamGuid).Set("1=" + BIMstatus1.ToString() + "=" + STstatus1.ToString()
                                            + "=" + dims.ToString() + "=" + point.ToString());
                                    }
                                }
                            }


                            
                            }
                            catch (Exception ex)
                            {
                                // Сбой на одном элементе не должен ронять обработку остальных
                                UpdaterDiagnostics.Report(UpdaterName, "элемент " + UpdaterUtils.IdText(id), ex);
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
            return UpdaterName;
        }
    }
}
