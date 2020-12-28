using Esprima;
using Jint.Native;
using ReactUnity.Interop;
using ReactUnity.DomProxies;
using ReactUnity.Schedulers;
using ReactUnity.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ReactUnity.Styling.Types;
using JavaScriptEngineSwitcher.Core;
using JavaScriptEngineSwitcher.Jint;
using JavaScriptEngineSwitcher.V8;
using JavaScriptEngineSwitcher.ChakraCore;

namespace ReactUnity
{
    public class ReactUnity : MonoBehaviour
    {
        public enum EngineType
        {
            Jint = 0,
            V8 = 1,
        }

        private IJsEngine engine;
        private UnityUGUIContext unityContext;
        private UnityScheduler scheduler;

        public StringObjectDictionary Globals = new StringObjectDictionary();
        public ReactScript Script = new ReactScript() { ScriptSource = ScriptSource.Resource, SourcePath = "react/index.js" };
        private ReactScript TestScript = new ReactScript() { ScriptSource = ScriptSource.Url, SourcePath = "http://localhost:9876/context.html", UseDevServer = false };

        public List<TextAsset> PreloadScripts = new List<TextAsset>();

        private IDisposable ScriptWatchDisposable;

        public RectTransform Root => transform as RectTransform;

        public EngineType JsEngine = EngineType.Jint;
        public string EngineName => JsEngine + "JsEngine";
        public bool EnableDebugging = false;
        public bool PauseDebuggerOnStart = false;

        void OnEnable()
        {
            CreateSwitcher();
            Restart();
        }

        void CreateSwitcher()
        {
            var engineSwitcher = JsEngineSwitcher.Current;
            engineSwitcher.EngineFactories
                .AddJint(x =>
                {
                    //e.CatchClrExceptions(ex =>
                    //{
                    //    var lastNode = engine.GetLastSyntaxNode();
                    //    Debug.LogError($"CLR exception in {lastNode.Location.Start.Line}:{lastNode.Location.Start.Column} - {lastNode.Location.End.Line}:{lastNode.Location.End.Column}");
                    //    Debug.LogError(ex);
                    //    return true;
                    //});
                    //e.SetTypeConverter(x => new NullableTypeConverter(x));
                })
                .AddV8(x =>
                {
                    x.DebugPort = 9222;
                    x.EnableDebugging = EnableDebugging;
                    x.EnableRemoteDebugging = EnableDebugging;
                    x.AwaitDebuggerAndPauseOnStart = PauseDebuggerOnStart;
                })
                .AddChakraCore(x =>
                {
                    x.EnableExperimentalFeatures = true;
                });

            engineSwitcher.DefaultEngineName = EngineName;
        }

        void OnDisable()
        {
            if (ScriptWatchDisposable != null) ScriptWatchDisposable.Dispose();
        }

        void Clean()
        {
            foreach (Transform children in Root)
            {
                DestroyImmediate(children.gameObject);
            }

            scheduler?.clearAllTimeouts();
        }

        private void LoadAndRun(ReactScript script, List<TextAsset> preload, Action callback = null, bool disableWarnings = false)
        {
            MainThreadDispatcher.Initialize();
            int debounce = -1;
            ScriptWatchDisposable = script.GetScript((code, isDevServer) =>
            {
                if (debounce >= 0) MainThreadDispatcher.StopDeferred(debounce);
                debounce = MainThreadDispatcher.Timeout(() => RunScript(code, isDevServer, script, preload, callback), 0.5f);
            }, out var result, true, disableWarnings);
            RunScript(result, false, script, preload, callback);
        }

        [ContextMenu("Restart")]
        public void Restart()
        {
            LoadAndRun(Script, PreloadScripts, null, false);
        }

        private void Test(bool debug = false)
        {
            var preload = new List<TextAsset>(PreloadScripts);
            preload.Add(Resources.Load<TextAsset>("ReactUnity/test/socket"));
            preload.Add(Resources.Load<TextAsset>("ReactUnity/test/karma"));
            preload.Add(Resources.Load<TextAsset>("ReactUnity/test/context"));
            preload.Add(Resources.Load<TextAsset>("ReactUnity/test/mocha"));
            preload.Add(Resources.Load<TextAsset>("ReactUnity/test/mocha-adapter"));
            preload.Add(Resources.Load<TextAsset>("ReactUnity/test/chai"));
            preload.Add(Resources.Load<TextAsset>("ReactUnity/test/chai-adapter"));
            if (debug) preload.Add(Resources.Load<TextAsset>("ReactUnity/test/debug"));

            LoadAndRun(TestScript, preload, null, true);
        }

        [ContextMenu("Test")]
        public void Test()
        {
            Test(false);
        }

        [ContextMenu("TestDebug")]
        public void TestDebug()
        {
            Test(true);
        }

        void RunScript(string script, bool isDevServer, ReactScript scriptObj, List<TextAsset> preload = null, Action callback = null)
        {
            if (string.IsNullOrWhiteSpace(script)) return;

            Clean();

            if (engine == null) CreateEngine();
            unityContext = new UnityUGUIContext(Root, engine, Globals, scriptObj, isDevServer);
            CreateLocation(engine, scriptObj);

            List<Action> callbacks = new List<Action>() { callback };

            engine.EmbedHostObject("addEventListener", new Action<string, Action>((e, f) =>
            {
                if (e == "DOMContentLoaded") callbacks.Add(f);
            }));

            engine.EmbedHostObject("Unity", new ReactUnityAPI(engine));
            engine.EmbedHostObject("RootContainer", unityContext.Host);
            engine.EmbedHostObject("Globals", Globals);
            try
            {
                if (preload != null) preload.ForEach(x => engine.Execute(x.text));
                engine.Execute(script, "react.js");
                callbacks.ForEach(x => x?.Invoke());
            }
            catch (ParserException ex)
            {
                Debug.LogError($"Parser exception in line {ex.LineNumber} column {ex.Column}");
                Debug.LogException(ex);
            }
            catch (JsException ex)
            {
                Debug.LogError(ex.Message);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public void ExecuteScript(string script)
        {
            engine.Execute(script);
        }

        void CreateEngine()
        {
            engine = JsEngineSwitcher.Current.CreateEngine(EngineName);

            engine.EmbedHostObject("log", new Func<object, object>((x) => { Debug.Log(x); return x; }));
            engine.Execute("__dirname = '';");
            engine.Execute("WeakMap = Map;");
            engine.Execute("globalThis = global = window = parent = this;");
            engine.Execute("setTimeout = setInterval = clearTimeout = clearInterval = null;");
            engine.Execute("btoa = atob = null;");
            engine.Execute("process = { env: { NODE_ENV: 'production' }, argv: [], on: () => {} };"); ;


            engine.EmbedHostObject("Engine", engine);
            engine.EmbedHostType("RealCallback", typeof(Callback));
            engine.Execute("Callback = function(fun) { return new RealCallback(fun, Engine); }");


            CreateConsole(engine);
            CreateLocalStorage(engine);
            CreateScheduler(engine);
            engine.EmbedHostType("YogaValue", typeof(Facebook.Yoga.YogaValue));
            engine.EmbedHostType("Color", typeof(Color));
            engine.EmbedHostType("ShadowDefinition", typeof(ShadowDefinition));
            engine.EmbedHostType("Vector2", typeof(Vector2));
            engine.EmbedHostType("Vector3", typeof(Vector3));
            engine.EmbedHostType("Rect", typeof(Rect));
            engine.EmbedHostType("RectOffset", typeof(RectOffset));
            engine.EmbedHostType("Action", typeof(Action));

            // Load polyfills
            engine.Execute(Resources.Load<TextAsset>("ReactUnity/polyfills/promise").text);
            engine.Execute(Resources.Load<TextAsset>("ReactUnity/polyfills/base64").text);
        }

        void CreateConsole(IJsEngine engine)
        {
            var console = new ConsoleProxy(engine);

            engine.EmbedHostObject("console", console);
        }

        void CreateScheduler(IJsEngine engine)
        {
            scheduler = new UnityScheduler();
            engine.EmbedHostObject("UnityScheduler", scheduler);
            engine.Execute("global.setTimeout = function setTimeout(fun, delay) { return UnityScheduler.setTimeout(new Callback(fun), delay); }");
            engine.Execute("global.setInterval = function setInterval(fun, delay) { return UnityScheduler.setInterval(new Callback(fun), delay); }");
            engine.Execute("global.setImmediate = function setImmediate(fun) { return UnityScheduler.setImmediate(new Callback(fun)); }");
            engine.Execute("global.requestAnimationFrame = function requestAnimationFrame(fun) { return UnityScheduler.requestAnimationFrame(new Callback(fun)); }");
            engine.EmbedHostObject("clearTimeout", new Action<int?>(scheduler.clearTimeout));
            engine.EmbedHostObject("clearInterval", new Action<int?>(scheduler.clearInterval));
            engine.EmbedHostObject("clearImmediate", new Action<int?>(scheduler.clearImmediate));
            engine.EmbedHostObject("cancelAnimationFrame", new Action<int?>(scheduler.cancelAnimationFrame));
        }

        void CreateLocalStorage(IJsEngine engine)
        {
            var storage = new LocalStorage();
            engine.EmbedHostObject("localStorage", storage);
        }

        void CreateLocation(IJsEngine engine, ReactScript script)
        {
            var location = new Location(script.SourceLocation, Restart);
            engine.EmbedHostObject("location", location);

#if UNITY_EDITOR
            engine.EmbedHostType("WebSocket", typeof(WebSocketProxy));
            engine.EmbedHostType("XMLHttpRequest", typeof(XMLHttpRequest));
            engine.Execute(@"(function() {
  var oldXMLHttpRequest = XMLHttpRequest;
  XMLHttpRequest = function() { return new oldXMLHttpRequest('" + location.origin + @"'); }
})();");
#endif
            engine.EmbedHostObject("document", new DocumentProxy(unityContext, this, location.origin));
        }
    }

    public class LocalStorage
    {
        public LocalStorage()
        {
        }

        public void setItem(string x, string value)
        {
            PlayerPrefs.SetString(x, value);
        }

        public string getItem(string x)
        {
            return PlayerPrefs.GetString(x, "");
        }
    }

    public class Location
    {
        public string href { get; }
        public string protocol { get; }
        public string hostname { get; }
        public string origin { get; }
        public string host { get; }
        public string port { get; }
        public string search { get; }
        public string pathname { get; }
        private Action restart { get; }

        public Location(string sourceLocation, Action restart)
        {
            var href = sourceLocation;
            var hrefSplit = href.Split(new string[] { "//" }, 2, StringSplitOptions.None);

            var protocol = hrefSplit.Length > 1 ? hrefSplit.First() : null;

            var hrefWithoutProtocol = hrefSplit.Length > 1 ? string.Join("", hrefSplit.Skip(1)) : href;
            var hrefWithoutProtocolSplit = hrefWithoutProtocol.Split(new string[] { "/" }, 2, StringSplitOptions.None);

            var host = hrefWithoutProtocolSplit.FirstOrDefault();
            var hostSplit = host.Split(new string[] { ":" }, 2, StringSplitOptions.None);
            var hostName = hostSplit.First();
            var port = hostSplit.ElementAtOrDefault(1) ?? "";

            var origin = protocol + "//" + host;
            var pathName = string.Join("", hrefWithoutProtocolSplit.Skip(1));

            this.href = href;
            this.protocol = protocol;
            this.hostname = hostName;
            this.origin = origin;
            this.host = host;
            this.port = port;
            this.search = "";
            this.pathname = pathName;
            this.restart = restart;
        }

        public void reload()
        {
            MainThreadDispatcher.OnUpdate(restart);
        }
    }
}
