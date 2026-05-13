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
    public class TNovHoleUpdater : IUpdater
    {
        static AddInId _appId;
        static UpdaterId _updaterId;

        public TNovHoleUpdater(AddInId id)
        {
            _appId = id;

            _updaterId = new UpdaterId(_appId, new Guid(
                                                   "43f11663-995f-4542-a756-0f4a400a813b"));
        }
        Guid adskGparamGuid = new Guid("3de5f1a4-d560-4fa8-a74f-25d250fb3401");//ADSK_Группирование
        Guid adskElev0paramGuid = new Guid("6ec2f9e9-3d50-4d75-a453-26ef4e6d1625");//ADSK_Отверстие_Отметка от нуля
        Guid adskElevLevelparamGuid = new Guid("e4793a44-6050-45b3-843e-cfb49d9191c5");//ADSK_Отверстие_Отметка от этажа
        Guid adskElevLevel2paramGuid = new Guid("44f7ce8a-2926-4514-bacb-423bd4ac3847");//ADSK_Отверстие_Отметка этажа
        Guid NTNovTextparamGuid = new Guid("b00446ce-acf8-498e-add9-a3603abe9028"); //N_TNov_Text
        Guid adskHoleWidthParamGuid = new Guid("096bc30e-3c95-4637-84d5-9f6bf45d8676");//ADSK_Отверстие_Ширина
        Guid adskHoleHeightParamGuid = new Guid("bc4e92d8-db66-4e93-8923-3af6e2dc8599");//ADSK_Отверстие_Высота
        Guid adskDiamParamGuid = new Guid("9b679ab7-ea2e-49ce-90ab-0549d5aa36ff");//ADSK_Размер_Диаметр
        Guid NTaskApprovedBIMParamGuid = new Guid("94587b6e-5bdd-4fe8-bea4-4996c32801c4");//N_Согласовано BIM
        Guid NTaskApprovedSTParamGuid = new Guid("7cb33aa5-8106-4e4c-8038-6691e34f438c");//N_Согласовано КР
        //04.2026: параметр N_Согласовано рук исключен как устаревший, для совместимости заполняется как "1"

        public void Execute(UpdaterData data)
        {
            Document doc = data.GetDocument();
            Autodesk.Revit.ApplicationServices.Application app = doc.Application;

            //параметры
            ElementId familyNameParamId = new ElementId(-1002002); //id параметра Имя семейства


            //проверка имени файла
            string docName = doc.Title.ToString();
            bool taskModel = false; if (docName.Contains("Задани") || docName.Contains("задани") || docName.Contains("-ЗД") || docName.Contains("_ЗД") || docName.Contains("ЗАДАНИЕ")) taskModel = true;

            List<ElementId> idsA = data.GetAddedElementIds().ToList();
            List<ElementId> idsM = data.GetModifiedElementIds().ToList();
            List<ElementId> ids = new List<ElementId>();

            ElementFilter elementFilter = (ElementFilter)new ElementParameterFilter(ParameterFilterRuleFactory.CreateContainsRule(familyNameParamId, "pmN.Отверстие", true));


            foreach (var id in idsA)
            {
                Element elem = doc.GetElement(id);
                if (elementFilter.PassesFilter(elem)) ids.Add(id);
            }
            foreach (var id in idsM)
            {
                Element elem = doc.GetElement(id);
                if (elementFilter.PassesFilter(elem)) ids.Add(id);
            }

            foreach (ElementId id in ids) //заполнение отметки
            {
                try
                {
                    Element elem = doc.GetElement(id);
                    double otm = elem.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM).AsDouble();
                    elem.get_Parameter(adskElevLevelparamGuid)?.Set(otm); //Отметка от уровня
                    Element level = doc.GetElement(elem.LevelId);
                    double elev = level.get_Parameter(BuiltInParameter.LEVEL_ELEV).AsDouble();
                    elem.get_Parameter(adskElevLevel2paramGuid)?.Set(elev); //Отметка уровня
                    elem.get_Parameter(adskElev0paramGuid)?.Set(otm + elev); //Отметка от нуля
                }
                catch (Exception) { }
            }

            if (taskModel) 
            {
                //проверка подключения к серверу
                string usagefilePath = nova.novaserver + "_TNov/usage.txt";
                bool servercheck = File.Exists(usagefilePath);

                if (servercheck)
                {
                    foreach (ElementId id in ids)
                    {
                        Element elem = doc.GetElement(id);
                        if (null != elem)
                        {
                            try
                            {
                                //заполнение группирования
                                if (elem.GroupId.IntegerValue != -1) //отверстие - в группе
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

                                        if (adskGvalue.Length > 0) elem.get_Parameter(adskGparamGuid)?.Set(adskGvalue);
                                    }
                                }

                                //система отслеживания

                                //имя и роль пользователя
                                string userName = app.Username;
                                string userDepartment = "-"; string userDepRole = "-";
                                string[] rolesFile = File.ReadAllLines("//fs-nova/Distr/0.For Admin/_TNov/roles.txt");
                                foreach (string role in rolesFile)
                                {
                                    if (role.Contains(userName))
                                    {
                                        string[] line = role.Split(','); userDepartment = line[1]; userDepRole = line[2]; break;
                                    }

                                }
                                Guid widthParam = adskHoleWidthParamGuid; Guid heightParam = adskHoleHeightParamGuid;
                                foreach (Parameter param in elem.ParametersMap) //круглые отв
                                {
                                    string paramName = param.Definition.Name;
                                    if (paramName == "ADSK_Размер_Диаметр") { widthParam = adskDiamParamGuid; heightParam = adskDiamParamGuid; }
                                }
                                string prevValue = "0";
                                bool TNovTextParamExist = Param.ParamExistByGuid(NTNovTextparamGuid, elem);
                                if (TNovTextParamExist) { try { prevValue = elem.get_Parameter(NTNovTextparamGuid).AsValueString(); } catch (Exception) { } }
                                bool prevValues = true;
                                if (prevValue == null || prevValue == "0") prevValues = false;
                                if (prevValues && elem.Location != null)
                                {
                                    //считываем предыдущие значения параметров
                                    ///структура значения параметра: СоглРук=СоглBIM=СоглКР=СуммаРазмеров=Координаты
                                    string[] pars = prevValue.Split('=');
                                    int Headstatus0 = 0; if (pars[0] == "1") Headstatus0 = 1;
                                    int BIMstatus0 = 0; if (pars.Length > 1 && pars[1] == "1") BIMstatus0 = 1;
                                    int STstatus0 = 0; if (pars.Length > 2 && pars[2] == "1") STstatus0 = 1;
                                    double dims0 = 0; if (pars.Length > 3 && pars[3].Length > 0) Double.TryParse(pars[3], out dims0);
                                    double point0 = 0; if (pars.Length > 4 && pars[4].Length > 0) Double.TryParse(pars[4], out point0);
                                    //считываем новые значения параметров
                                    int BIMstatus = elem.get_Parameter(NTaskApprovedBIMParamGuid).AsInteger();
                                    int STstatus = elem.get_Parameter(NTaskApprovedSTParamGuid).AsInteger();
                                    double elem_width = elem.get_Parameter(widthParam).AsDouble(); double elem_height = elem.get_Parameter(heightParam).AsDouble();
                                    double dims = elem_width * 0.3048 * 1000000 + elem_height * 0.3048 * 1000; dims = Math.Round(dims);
                                    LocationPoint elem_lp = (LocationPoint)elem.Location;
                                    XYZ p = elem_lp.Point; double point = p.X * 0.3048 * 1000000000 + p.Y * 0.3048 * 1000000 + p.Z * 0.3048 * 1000; point = Math.Round(point);
                                    //формируем новые значения параметров для записи
                                    int BIMstatus1 = BIMstatus; int STstatus1 = STstatus;
                                    //изменяем значения параметров при необходимости

                                    int issues = 0;

                                    
                                    if (pars.Length > 1 && BIMstatus != BIMstatus0) //Согласовано BIM
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
                                            case "BIM": //добавлено 10.2025
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

                                    if (pars.Length > 3 && dims != dims0)//сумма размеров
                                    {
                                        //MessageBox.Show("размеры");
                                        switch (userDepartment)
                                        {
                                            case "BIM":
                                                /*if (STstatus == 1)
                                                {
                                                    STstatus1 = 0; elem.LookupParameter(NTaskApprovedSTParamGuid).Set(STstatus1);
                                                }*/
                                                break; //изменено 10.2025

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
                                                    STstatus1 = 0; elem.LookupParameter(NTaskApprovedSTParamGuid).Set(STstatus1);
                                                }*/
                                                break; //изменено 10.2025

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
                                    elem.LookupParameter("N_TNov_Text").Set("1=" + BIMstatus1.ToString() + "=" + STstatus1.ToString()
                                        + "=" + dims.ToString() + "=" + point.ToString());





                                }
                                else
                                {
                                    if (elem.Location != null)
                                    {
                                        int BIMstatus = elem.get_Parameter(NTaskApprovedBIMParamGuid).AsInteger();
                                        int STstatus = elem.get_Parameter(NTaskApprovedSTParamGuid).AsInteger();
                                        double elem_width = elem.get_Parameter(widthParam).AsDouble(); double elem_height = elem.get_Parameter(heightParam).AsDouble();
                                        double dims = elem_width * 0.3048 * 1000000 + elem_height * 0.3048 * 1000; dims = Math.Round(dims);
                                        LocationPoint elem_lp = (LocationPoint)elem.Location;
                                        XYZ p = elem_lp.Point; double point = p.X * 0.3048 * 1000000000 + p.Y * 0.3048 * 1000000 + p.Z * 0.3048 * 1000; point = Math.Round(point);

                                        int BIMstatus1 = BIMstatus; int STstatus1 = STstatus;
                                        /*
                                        switch (userDepartment)
                                        {
                                            case "BIM":
                                                break;

                                            case "ST":
                                                break;

                                            default:
                                                if (STstatus == 1) {elem.LookupParameter(NTaskApprovedSTParamGuid).Set(0); STstatus1=0;}
                                                if (BIMstatus == 1) {elem.LookupParameter(NTaskApprovedBIMParamGuid).Set(0); BIMstatus1=0;}
                                                break;
                                        }
                                        */
                                        if (Param.ParamExistByGuid(NTNovTextparamGuid, elem))
                                        {
                                            //записываем новые значения параметров
                                            elem.get_Parameter(NTNovTextparamGuid).Set("1=" + BIMstatus1.ToString() + "=" + STstatus1.ToString()
                                                + "=" + dims.ToString() + "=" + point.ToString());
                                        }
                                    }
                                }

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
            return "TNovHoleUpdater";
        }
    }
}
