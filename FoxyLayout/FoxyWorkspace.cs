using NinjaTrader.Gui.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

using Serilog.Events;
using static FoxyLayout.FoxyUtilities;
using NinjaTrader.Gui;
using NinjaTrader.Core;

namespace FoxyLayout
{ 
    public class FoxyWorkspace : NTWindow, IWorkspacePersistence
    {
        public FoxyWorkspace()
        {
            Height                  = 0;
            Width                   = 0;
            Top                     = 0;
            Left                    = 0;
            WindowStartupLocation   = WindowStartupLocation.Manual;
            WindowStyle             = WindowStyle.None;
            ShowInTaskbar           = false;
            Background              = System.Windows.Media.Brushes.Transparent;
            AllowsTransparency      = true;

            Loaded += (o, e) =>
            {
                if (WorkspaceOptions == null)
                    WorkspaceOptions = new WorkspaceOptions($"FoxyWorkspace-{Guid.NewGuid():N}", this);
            };
        }

        public void Restore(XDocument document, XElement element)
        {
            try
            {
                lock (document)
                {
                    if (document.Root == null || element == null)
                        return;

                    string workspaceName = element.Element("WorkspaceName")?.Value;
                    FoxyLog($"FoxyWorkspace.Restore() called: {workspaceName}", LogEventLevel.Debug);
                    if (workspaceName != null)
                    {
                        lock (FoxyLayoutAddOn.FoxyRestore)
                        {
                            if (!FoxyLayoutAddOn.FoxyRestore.ContainsKey(workspaceName))
                                FoxyLayoutAddOn.FoxyRestore.Add(workspaceName, new XElement(element));
                            else
                                FoxyLayoutAddOn.FoxyRestore[workspaceName] = new XElement(element); // element is a reference and will get messed with by NT so lets keep loose copies
                        }
                        lock (FoxyLayoutAddOn.FoxyWindows)
                            if (FoxyLayoutAddOn.FoxyWindows.ContainsKey(workspaceName))
                                foreach (FoxyWindow foxyWindow in FoxyLayoutAddOn.FoxyWindows[workspaceName])
                                    foxyWindow.RestoreFromXml(workspaceName);
                    }
                }
            }
            catch (Exception e)
            {
                FoxyLog(e.ToString(), LogEventLevel.Error);
            }
        }

        public void Save(XDocument document, XElement element)
        {
            FoxyLog($"FoxyWorkspace.Save() called: {WorkspaceOptions.WorkspaceName}", LogEventLevel.Debug);
            if (document == null || element == null)
                return;
            try
            {
                lock (document)
                {
                    if (element.Element("WorkspaceName") != null)
                        element.Element("WorkspaceName").Value = WorkspaceOptions.WorkspaceName;
                    else
                        element.Add(new XElement("WorkspaceName", WorkspaceOptions.WorkspaceName)); // WorkspaceOptions will be null when Restore is called :(

                    lock (FoxyLayoutAddOn.FoxyWindows)
                    {
                        foreach (FoxyWindow foxyWindow in FoxyLayoutAddOn.FoxyWindows[WorkspaceOptions.WorkspaceName])
                        {
                            if (foxyWindow.Window != this && foxyWindow.IsLocked)
                            {
                                XElement windowElement = new XElement((foxyWindow.Window as IWorkspacePersistence)?.WorkspaceOptions?.PersistenceId ?? foxyWindow.Window.ToString());
                                if (foxyWindow.Window is ControlCenter)
                                    windowElement.Add(new XElement("Bounds", foxyWindow.Bounds));
                                if (element.Element(windowElement.Name) != null)
                                    element.Element(windowElement.Name).Remove();
                                element.Add(windowElement);
                            }
                        }
                    }
                    document.Save(WorkspaceOptions.WorkspacesFilePath);
                }
            }
            catch (Exception e)
            {
                FoxyLog(e.ToString(), LogEventLevel.Error);
            }
        }

        public WorkspaceOptions WorkspaceOptions { get; set; }
    }
}
