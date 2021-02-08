using System;
using System.Collections;
using UnityEngine;

namespace ReactUnity
{
    [Serializable]
    public class ReactScript
    {
        public ScriptSource ScriptSource = ScriptSource.TextAsset;

        public TextAsset SourceAsset;
        public string SourcePath;
        public string SourceText;
        public string ResourcesPath;

#pragma warning disable CS0414
        [SerializeField]
        [Tooltip(@"Editor only. Watches file for changes and refreshes the view on change.
Can be enabled outside the editor by adding define symbol REACT_WATCH_OUTSIDE_EDITOR to build.")]
        private bool Watch = false;
#pragma warning restore CS0414

        public bool UseDevServer = true;
        public string DevServer = "http://localhost:3000";
        static string DevServerFilename = "";
        public string DevServerFile => DevServer + DevServerFilename;

        private bool SourceIsTextAsset => ScriptSource == ScriptSource.TextAsset;
        private bool SourceIsPath => ScriptSource != ScriptSource.TextAsset && ScriptSource != ScriptSource.Text;
        private bool SourceIsText => ScriptSource == ScriptSource.Text;
        private bool SourceIsWatchable => ScriptSource != ScriptSource.Url && ScriptSource != ScriptSource.Text;

        public string SourceLocation
        {
            get
            {
#if UNITY_EDITOR
                if (UseDevServer && !string.IsNullOrWhiteSpace(DevServer)) return DevServerFile;
#endif
                return GetResolvedSourcePath();
            }
        }


        public string GetResolvedSourcePath()
        {
            string path = "";

            if (ScriptSource == ScriptSource.File || ScriptSource == ScriptSource.Url)
                path = SourcePath;
            else if (ScriptSource == ScriptSource.TextAsset)
                path = ResourcesPath ?? "Assets/Resources/react/index.js";
            else if (ScriptSource == ScriptSource.Resource)
                path = SourcePath;
            return path;
        }

#if UNITY_EDITOR || REACT_WATCH_OUTSIDE_EDITOR
        IDisposable StartWatching(Action<string, bool> callback)
        {
            string path = GetResolvedSourcePath();
            if (string.IsNullOrWhiteSpace(path)) return null;

            return DetectChanges.WatchFileSystem(path, x => callback(System.IO.File.ReadAllText(path), false));
        }
#endif

        public IDisposable GetScript(Action<string, bool> changeCallback, out string result, bool useDevServer = true, bool disableWarnings = false)
        {
#if UNITY_EDITOR
            if (useDevServer && UseDevServer && !string.IsNullOrWhiteSpace(DevServer))
            {
                result = null;
                var request = UnityEngine.Networking.UnityWebRequest.Get(DevServerFile);
                return new Interop.MainThreadDispatcher.CoroutineHandle(
                    Interop.MainThreadDispatcher.StartDeferred(WatchWebRequest(request, changeCallback, err =>
                    {
                        Debug.LogWarning("DevServer seems to be unaccessible. Falling back to the original script.");
                        GetScript(changeCallback, out var dummyResult, false);
                        if (!string.IsNullOrWhiteSpace(dummyResult)) changeCallback(dummyResult, false);
                    }, true)));
            }
#endif

            switch (ScriptSource)
            {
                case ScriptSource.TextAsset:
                    if (!SourceAsset) result = null;
#if UNITY_EDITOR
                    else result = System.IO.File.ReadAllText(UnityEditor.AssetDatabase.GetAssetPath(SourceAsset));
#else
                    else result = SourceAsset.text;
#endif
                    break;
                case ScriptSource.File:
#if UNITY_EDITOR || REACT_FILE_API
#if !REACT_FILE_API
                    if(!disableWarnings) Debug.LogWarning("REACT_FILE_API is not defined. Add REACT_FILE_API to build symbols to if you want to use this feature outside editor.");
#endif
                    result = System.IO.File.ReadAllText(SourcePath);
                    break;
#else
                    throw new Exception("REACT_FILE_API must be defined to use File API outside the editor. Add REACT_FILE_API to build symbols to use this feature.");
#endif
                case ScriptSource.Url:
#if UNITY_EDITOR || REACT_URL_API
#if !REACT_URL_API
                    if (!disableWarnings) Debug.LogWarning("REACT_URL_API is not defined. Add REACT_URL_API to build symbols to if you want to use this feature outside editor.");
#endif
                    result = null;
                    var request = UnityEngine.Networking.UnityWebRequest.Get(SourcePath);
                    return new Interop.MainThreadDispatcher.CoroutineHandle(
                        Interop.MainThreadDispatcher.StartDeferred(WatchWebRequest(request, changeCallback)));
#else
                    throw new Exception("REACT_URL_API must be defined to use Url API outside the editor. Add REACT_URL_API to build symbols to use this feature.");
#endif
                case ScriptSource.Resource:
                    var asset = Resources.Load(SourcePath) as TextAsset;
                    if (asset) result = asset.text;
                    else result = null;
                    break;
                case ScriptSource.Text:
                    result = SourceText;
                    break;
                default:
                    result = null;
                    break;
            }

#if UNITY_EDITOR || REACT_WATCH_OUTSIDE_EDITOR
            if (Watch && SourceIsWatchable) return StartWatching(changeCallback);
#endif
            return null;
        }

#if UNITY_EDITOR || REACT_URL_API
        private IEnumerator WatchWebRequest(
            UnityEngine.Networking.UnityWebRequest request,
            Action<string, bool> callback,
            Action<string> errorCallback = null,
            bool isDevServer = false
        )
        {
            yield return request.SendWebRequest();
            if (!string.IsNullOrWhiteSpace(request.error))
                errorCallback?.Invoke(request.error);
            else
                callback(request.downloadHandler.text, isDevServer);
        }
#endif
    }

    public enum ScriptSource
    {
        TextAsset = 0,
        File = 1,
        Url = 2,
        Resource = 3,
        Text = 4,
    }


#if UNITY_EDITOR || REACT_WATCH_OUTSIDE_EDITOR
    public class DetectChanges
    {
        public static IDisposable WatchFileSystem(string path, Action<string> callback)
        {
            System.IO.FileSystemWatcher fileSystemWatcher = new System.IO.FileSystemWatcher();

            fileSystemWatcher.Path = System.IO.Path.GetDirectoryName(path);
            fileSystemWatcher.Filter = System.IO.Path.GetFileName(path);
            fileSystemWatcher.NotifyFilter = System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.Size;

            fileSystemWatcher.Changed += (x, y) => callback(y.FullPath);
            fileSystemWatcher.EnableRaisingEvents = true;

            return fileSystemWatcher;
        }
    }
#endif
}
