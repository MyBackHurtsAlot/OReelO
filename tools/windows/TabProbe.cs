using System;
using System.Diagnostics;
using System.Windows.Automation;

internal static class TabProbe
{
    private static int Main()
    {
        Process[] processes = Process.GetProcessesByName("Adobe Premiere Pro");
        if (processes.Length == 0)
        {
            Console.Error.WriteLine("Premiere is not running.");
            return 1;
        }

        foreach (Process process in processes)
        {
            try
            {
                AutomationElement root = AutomationElement.RootElement.FindFirst(
                    TreeScope.Children,
                    new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id));
                if (root == null)
                {
                    Console.Error.WriteLine("Premiere PID {0}: window not visible to UI Automation.", process.Id);
                    continue;
                }

                AutomationElementCollection tabs = root.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));

                Console.WriteLine("Premiere PID {0}: {1} tab items", process.Id, tabs.Count);
                foreach (AutomationElement tab in tabs)
                {
                    Console.WriteLine("{0}\t{1}\t{2}",
                        tab.Current.Name,
                        tab.Current.AutomationId,
                        tab.Current.ClassName);
                }

                if (tabs.Count == 0)
                {
                    AutomationElementCollection named = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                    int printed = 0;
                    foreach (AutomationElement element in named)
                    {
                        string name = element.Current.Name;
                        if (String.IsNullOrWhiteSpace(name)) continue;
                        Console.WriteLine("RAW\t{0}\t{1}\t{2}",
                            element.Current.ControlType.ProgrammaticName,
                            name,
                            element.Current.ClassName);
                        if (++printed == 500) break;
                    }
                    Console.WriteLine("Premiere PID {0}: {1} named raw elements shown", process.Id, printed);
                }
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("Premiere PID {0}: {1}: {2}",
                    process.Id, error.GetType().Name, error.Message);
            }
        }

        return 0;
    }
}
