﻿using Sync.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using static Sync.Tools.DefaultI18n;
using Sync.Command;
using Sync.Client;
using Sync.Source;

namespace Sync.Plugins
{

    /// <summary>
    /// flag
    /// </summary>
    public interface IPluginEvent : IBaseEvent { }

    /// <summary>
    /// Base plugin events
    /// </summary>
    public class PluginEvents : BaseEventDispatcher<IPluginEvent>
    {
        /// <summary>
        /// Fire when init plugin
        /// </summary>
        public struct InitPluginEvent : IPluginEvent
        {
            public Plugin Plugin { get; private set; }
            public InitPluginEvent(Plugin plugin)
            {
                this.Plugin = plugin;
            }
        }
        /// <summary>
        /// Fire when init source
        /// </summary>
        public struct InitSourceEvent : IPluginEvent
        {
            public SourceManager Sources { get; private set; }
            public InitSourceEvent(SourceManager source)
            {
                Sources = source;
            }
        }
        /// <summary>
        /// Fire when init filter
        /// </summary>
        public struct InitFilterEvent : IPluginEvent
        {
            public FilterManager Filters { get; private set; }
            public InitFilterEvent(FilterManager filters)
            {
                Filters = filters;
            }
        }
        /// <summary>
        /// Fire when init command
        /// </summary>
        public struct InitCommandEvent : IPluginEvent
        {
            public CommandManager Commands { get; private set; }
            public InitCommandEvent(CommandManager commands)
            {
                Commands = commands;
            }
        }
        /// <summary>
        /// Fire when init clients
        /// </summary>
        public struct InitClientEvent : IPluginEvent
        {
            public ClientManager Clients { get; private set; }
            public InitClientEvent(ClientManager clients)
            {
                Clients = clients;
            }
        }
        /// <summary>
        /// Fire when init source warpper
        /// </summary>
        public struct InitSourceWarpperEvent : IPluginEvent
        {
            public SourceWorkWrapper SourceWrapper { get; private set; }
            public InitSourceWarpperEvent(SourceWorkWrapper wrapper)
            {
                SourceWrapper = wrapper;
            }
        }
        /// <summary>
        /// Fire when init client warpper
        /// </summary>
        public struct InitClientWarpperEvent : IPluginEvent
        {
            public ClientWorkWrapper ClientWrapper { get; private set; }
            public InitClientWarpperEvent(ClientWorkWrapper wrapper)
            {
                ClientWrapper = wrapper;
            }
        }
        /// <summary>
        /// Fire when load complete
        /// </summary>
        public struct LoadCompleteEvent : IPluginEvent
        {
            public SyncHost Host { get; private set; }
            public LoadCompleteEvent(SyncHost host)
            {
                Host = host;
            }
        }

        /// <summary>
        /// Fire when ready
        /// </summary>
        public struct ProgramReadyEvent : IPluginEvent
        {
            //public SyncManager Manager { get; private set; }
            //public SyncManagerCompleteEvent()
            //{
            //    Manager = Program.host.SyncInstance;
            //}
        }

        public static readonly PluginEvents Instance = new PluginEvents();
        private PluginEvents()
        {
            EventDispatcher.Instance.RegisterNewDispatcher(GetType());
        }
    }

    public class PluginManager
    {

        List<Plugin> pluginList;
        private List<Assembly> asmList;
        private LinkedList<Type> loadedList;
        private List<Type> allList;
        internal PluginManager()
        {

        }

        internal int LoadCommnads()
        {
            PluginEvents.Instance.RaiseEvent(new PluginEvents.InitCommandEvent(SyncHost.Instance.Commands));
            return SyncHost.Instance.Commands.Dispatch.count;
        }

        internal int LoadSources()
        {
            PluginEvents.Instance.RaiseEvent(new PluginEvents.InitSourceEvent(SyncHost.Instance.Sources));
            return SyncHost.Instance.Sources.SourceList.Count();
        }

        internal int LoadFilters()
        {
            PluginEvents.Instance.RaiseEvent(new PluginEvents.InitFilterEvent(SyncHost.Instance.Filters));
            return SyncHost.Instance.Filters.Count;
        }

        internal int LoadClients()
        {
            PluginEvents.Instance.RaiseEvent(new PluginEvents.InitClientEvent(SyncHost.Instance.Clients));
            return SyncHost.Instance.Clients.Count;
        }

        internal void ReadySync()
        {
            PluginEvents.Instance.RaiseEvent(new PluginEvents.ProgramReadyEvent());
        }


        public IEnumerable<Plugin> GetPlugins()
        {
            return pluginList;
        }

        internal void ReadyProgram()
        {
            PluginEvents.Instance.RaiseEvent(new PluginEvents.LoadCompleteEvent(SyncHost.Instance));
        }

        internal int LoadPlugins()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");

            pluginList = new List<Plugin>();
            asmList = new List<Assembly>();
            asmList.AddRange(AppDomain.CurrentDomain.GetAssemblies());
            if (!Directory.Exists(path)) return 0;
            Directory.SetCurrentDirectory(path);


            foreach (string file in Directory.GetFiles(path, "*.dll", SearchOption.AllDirectories))
            {
                try
                {
                    if (asmList.Where(a => a.Location == file).Count() != 0)
                        continue;
                    Assembly asm = Assembly.LoadFrom(file);
                    asmList.Add(asm);
                }
                catch(Exception e)
                {
                    IO.CurrentIO.WriteColor(String.Format(LANG_LoadPluginErr, file, e.Message), ConsoleColor.Red);
                    continue;
                }
            }

            loadedList = new LinkedList<Type>();
            List<Type> lazylist = new List<Type>();
            allList = new List<Type>();
            //Load all plugins first
            foreach (Assembly asm in asmList)
            {
                foreach (Type item in asm.GetExportedTypes())
                {
                    Type it = asm.GetType(item.FullName);
                    if (it == null ||
                        !it.IsClass || !it.IsPublic ||
                        !typeof(Plugin).IsAssignableFrom(it) ||
                        typeof(Plugin) == it)
                        continue;
                    allList.Add(it);
                }
            }

            lazylist = allList.ToList();
            //looping add for resolve dependency
            do
            {

                lazylist = layerLoader(lazylist);

            } while (lazylist.Count != 0);

            return pluginList.Count;
        }

        private List<Type> layerLoader(IList<Type> asmList)
        {
            List<Type> nextLoad = new List<Type>();
            foreach (Type it in asmList)
            {
                try
                {

                    if (Check_Should_Late_Load(it))
                    {
#if (DEBUG)
                        IO.CurrentIO.WriteColor($"Lazy load [{it.Name}]", ConsoleColor.Green);
#endif
                        nextLoad.Add(it);
                        //Dependency load at this time
                        //Lazy load this plugin at next time
                        continue;
                    }


                    //no dependencies or dependencies all was loaded
                    if (!it.IsSubclassOf(typeof(Plugin))) continue;
                    else
                    {
                        LoadPluginFormType(it);
                        loadedList.AddLast(it);
                    }

                }
                catch (Exception e)
                {
                    IO.CurrentIO.WriteColor(String.Format(LANG_NotPluginErr, it.Name, e.Message), ConsoleColor.Red);
                    continue;
                }
            }

            return nextLoad;
        }

        private bool Check_Should_Late_Load(Type a)
        {

            SyncRequirePlugin requireAttr = a.GetCustomAttribute<SyncRequirePlugin>();
            SyncSoftRequirePlugin softRequirePlugin = a.GetCustomAttribute<SyncSoftRequirePlugin>();

            if (requireAttr != null)
            {
                foreach (var item in requireAttr.RequirePluguins)
                {
                    //Dependency was been loaded
                    if (loadedList.Contains(item)) continue;
                    else
                    {

                        //Check cycle reference
                        if (Check_A_IS_Reference_TO_B(item, a)) return false;
                        else return true;
                    }
                }
            }
            
            if(softRequirePlugin != null)
            {
                foreach (var item in softRequirePlugin.RequirePluguins)
                {
                    Type s = allList.FirstOrDefault(p => p.Name == item);
                    if (s == null)
                    {
                        continue;
                    }
                    else
                    {
                        if (Check_A_IS_Reference_TO_B(s, a)) return false;
                        if (!loadedList.Contains(s)) return true;
                    }
                }
            }

            return false;
        }

        private bool Check_A_IS_Reference_TO_B(Type a, Type b)
        {
            return Check_A_IS_HardReference_TO_B(a, b) || Check_A_IS_SoftReference_TO_B(a, b.Name);
        }

        private bool Check_A_IS_HardReference_TO_B(Type a, Type b)
        {
            SyncRequirePlugin refRequireCheck = a.GetCustomAttribute<SyncRequirePlugin>();
            if (refRequireCheck == null) return false;
            return refRequireCheck.RequirePluguins.Contains(b);
        }

        private bool Check_A_IS_SoftReference_TO_B(Type a, string b)
        {
            SyncSoftRequirePlugin refRequireCheck = a.GetCustomAttribute<SyncSoftRequirePlugin>();
            if (refRequireCheck == null) return false;
            return refRequireCheck.RequirePluguins.Contains(b);
        }

        private Plugin LoadPluginFormType(Type it)
        {
            object pluginTest = it.Assembly.CreateInstance(it.FullName);
            if (pluginTest == null)
            {
                throw new NullReferenceException();
            }

            Plugin plugin = (Plugin)pluginTest;
            IO.CurrentIO.WriteColor(String.Format(LANG_LoadingPlugin, plugin.Name), ConsoleColor.White);

            pluginList.Add(plugin);
            plugin.OnEnable();
            PluginEvents.Instance.RaiseEventAsync(new PluginEvents.InitPluginEvent(plugin));
            return plugin;
        }
    }

    /// <summary>
    /// Using this attribute when you want load some plugin before your plugin.
    /// </summary>
    public class SyncRequirePlugin : Attribute
    {
        public IReadOnlyList<Type> RequirePluguins;

        public SyncRequirePlugin(params Type[] types)
        {
            RequirePluguins = new List<Type>(types);
        }
    }

    /// <summary>
    /// Using this attribute when you dependence some plugin without hard link.
    /// </summary>
    public class SyncSoftRequirePlugin : Attribute
    {
        public IReadOnlyList<string> RequirePluguins;
        public SyncSoftRequirePlugin(params string[] types)
        {
            RequirePluguins = new List<string>(types);
        }
    }
}
