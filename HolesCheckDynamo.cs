using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using TNovCommon;

namespace TNov
{

    [Transaction(TransactionMode.Manual)]
    public class HolesCheckDynamo : IExternalCommand
    {
        
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (RevitAPI.UiApplication == null) { RevitAPI.Initialize(commandData); } UIApplication uiApp = RevitAPI.UiApplication;
            
                var info1 = new InfoWindow280("Сейчас откроется Проигрыватель Dynamo.\nВ нем найдите и запустите скрипт Чек-лист.Отверстия."); info1.ShowDialog();
                RevitCommandId id_built_in = RevitCommandId.LookupPostableCommandId(PostableCommand.DynamoPlayer);
                uiApp.PostCommand(id_built_in);
                
            
            
            return Result.Succeeded;
        }
    }
    
}
