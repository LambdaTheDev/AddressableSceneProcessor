using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Managing.Scened;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Core.Scenes
{
    // Scene processor written for Addressable scene loading
    public class AddressableSceneProcessor : SceneProcessorBase
    {
        // THIS IS USED FOR UNLOADING, DO NOT CLEAR AFTER SUCCESSFUL LOADING!!!
        protected readonly Dictionary<int, AsyncOperationHandle<SceneInstance>> LoadedScenesByHandle = new (4);
        private readonly List<Scene> _loadedScenes = new (4);

        protected readonly List<AsyncOperationHandle<SceneInstance>> LoadingAsyncOperations = new(4);
        protected AsyncOperationHandle<SceneInstance> CurrentAsyncOperation;

        // Here are stored scene's AssetReferences
        protected Dictionary<string, AssetReference> CompiledAddressableReferences;
        [SerializeField] private SceneReference[] rawSceneReferences;

        private void Awake()
        {
            // Get raw references, bind them into dictionary
            Dictionary<string, AssetReference> compiledReferences = new Dictionary<string, AssetReference>(rawSceneReferences.Length);
            for (int i = 0; i < rawSceneReferences.Length; i++)
            {
                compiledReferences.Add(rawSceneReferences[i].name, rawSceneReferences[i].reference);
            }

            CompiledAddressableReferences = compiledReferences;
        }

        public override void LoadStart(LoadQueueData queueData)
        {
            ResetProcessor();
        }

        public override void LoadEnd(LoadQueueData queueData)
        {
            ResetProcessor();
        }

        private void ResetProcessor()
        {
            CurrentAsyncOperation = default;
            LoadingAsyncOperations.Clear();
        }

        public override void BeginLoadAsync(string sceneName, LoadSceneParameters parameters)
        {
            // Try get reference
            if (!CompiledAddressableReferences.TryGetValue(sceneName, out AssetReference sceneReference))
                throw new ArgumentException($"Scene with name: {sceneName} is not registered in AddressableSceneProcessor!", nameof(sceneName));
                
            // Try load scene with Addressables
            AsyncOperationHandle<SceneInstance> loadHandle = Addressables.LoadSceneAsync(sceneReference, parameters.loadSceneMode, false);
            
            // And register this handle in systems
            LoadingAsyncOperations.Add(loadHandle);
            CurrentAsyncOperation = loadHandle;
        }

        public override void BeginUnloadAsync(Scene scene)
        {
            if (LoadedScenesByHandle.TryGetValue(scene.handle, out var loadHandle))
            {
                AsyncOperationHandle<SceneInstance> unloadHandle = Addressables.UnloadSceneAsync(loadHandle);
                CurrentAsyncOperation = unloadHandle;
                StartCoroutine(UnloadSceneAsync(loadHandle, scene.handle));
            }
            else
            {
                Debug.LogWarning("Tried to unload scene (name=" + scene.name + "), through SceneProcessor, although load reference could not been found!");
            }
        }

        private IEnumerator UnloadSceneAsync(AsyncOperationHandle<SceneInstance> unloadHandle, int sceneHandle)
        {
            yield return unloadHandle;
            LoadedScenesByHandle.Remove(sceneHandle);
        }

        public override bool IsPercentComplete()
        {
            return GetPercentComplete() >= 0.9;
        }

        public override float GetPercentComplete()
        {
            if (CurrentAsyncOperation.IsValid())
                return CurrentAsyncOperation.PercentComplete;

            return 1f;
        }

        public override List<Scene> GetLoadedScenes() => _loadedScenes;

        public override void AddLoadedScene(Scene scene)
        {
            throw new Exception("This method is UNSUPPORTED for this processor. Use AddLoadedScene(Scene, AsyncOperationHandle<SceneInstance>) overload!");
        }

        public void AddLoadedScene(Scene scene, AsyncOperationHandle<SceneInstance> loadHandle)
        {
            _loadedScenes.Add(scene);
            LoadedScenesByHandle.Add(scene.handle, loadHandle);
        }

        public override void ActivateLoadedScenes()
        {
            StartCoroutine(ActivateLoadedScenesAsync());
        }

        private IEnumerator ActivateLoadedScenesAsync()
        {
            foreach (var loadingAsyncOp in LoadingAsyncOperations)
            {
                yield return loadingAsyncOp.Result.ActivateAsync();
            }
        }

        public override IEnumerator AsyncsIsDone()
        {
            bool notDone;
            do
            {
                notDone = false;
                foreach (AsyncOperationHandle<SceneInstance> ao in LoadingAsyncOperations)
                {

                    if (!ao.IsDone)
                    {
                        notDone = true;
                        break;
                    }
                }
                yield return null;
            } while (notDone);
            
            // All loading async ops are done, let's load the scenes into system
            foreach (AsyncOperationHandle<SceneInstance> ao in LoadingAsyncOperations)
            {
                int loadedSceneIndex = ao.Result.Scene.handle;
                LoadedScenesByHandle.Add(loadedSceneIndex, ao);
            }
        }


        // A raw container for name -> scene reference
        [Serializable]
        private class SceneReference
        {
            public string name;
            public AssetReference reference;
        }
    }
}
