using Autodesk.Revit.UI;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace MrezaCuttingPlan
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                string panelName = "Armatura";

                RibbonPanel? panel = null;
                foreach (var p in application.GetRibbonPanels())
                {
                    if (p.Name == panelName)
                    {
                        panel = p;
                        break;
                    }
                }
                panel ??= application.CreateRibbonPanel(panelName);

                string assemblyPath = Assembly.GetExecutingAssembly().Location;

                var buttonData = new PushButtonData(
                    "PlanRezanjaMreza",
                    "Plan Rezanja\nMreža",
                    assemblyPath,
                    "MrezaCuttingPlan.Commands.GenerateCuttingPlanCommand"
                )
                {
                    ToolTip = "Generira plan rezanja armaturnih mreža (Q i R tipovi) iz FabricSheet elemenata.",
                    LongDescription = "Čita FabricSheet/FabricArea elemente, grupira po tipu mreže, " +
                                      "optimizira raspored unutar standardnih limova i generira PDF izvještaj."
                };

                panel.AddItem(buttonData);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("MrezaCuttingPlan – Greška pokretanja", ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
