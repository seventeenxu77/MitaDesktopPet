using System.IO;
using DesktopPet;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DesktopPetEditor
{
    public static class DesktopPetSetup
    {
        private const string SourcePrefabPath = "Assets/learn/MitaDreamer.prefab";
        private const string IdleClipPath = "Assets/AnimationClip/MitaDreamer.anim";
        private const string DragStartClipPath = "Assets/DesktopPet/Animations/DragLiftStart.anim";
        private const string DragLoopClipPath = "Assets/DesktopPet/Animations/DragFlailLoop.anim";
        private const string SitEnterClipPath = "Assets/AnimationClip/Mita Sit NormalStart.anim";
        private const string SitLoopClipPath = "Assets/AnimationClip/Mita Sit Normal.anim";
        private const string UiFontPath = "Assets/Font/FontJapaneseChinese.ttf";

        private const string RootFolder = "Assets/DesktopPet";
        private const string AnimatorPath = RootFolder + "/Animators/DesktopMita.controller";
        private const string PrefabPath = RootFolder + "/Prefabs/DesktopMita.prefab";
        private const string ScenePath = RootFolder + "/Scenes/DesktopPetPrototype.unity";
        private const string PersonaPath = RootFolder + "/Config/DesktopMitaPersona.asset";
        private const string BuildFolder = "Builds/DesktopPet";
        private const string BuildPath = BuildFolder + "/DesktopPet.exe";

        [MenuItem("Tools/Desktop Pet/更新桌宠", false, 10)]
        public static void CreateOrRefreshPrototype()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EnsureFolder(RootFolder + "/Animators");
            EnsureFolder(RootFolder + "/Animations");
            EnsureFolder(RootFolder + "/Config");
            EnsureFolder(RootFolder + "/Prefabs");
            EnsureFolder(RootFolder + "/Scenes");

            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            if (sourcePrefab == null)
            {
                throw new FileNotFoundException("Desktop Pet source prefab was not found.", SourcePrefabPath);
            }

            CreateOrRefreshDragAnimations(sourcePrefab);
            DesktopPetCrossLegAuthoring.Generate();
            var controller = CreateAnimatorController();
            DesktopPetCrossLegAuthoring.ConfigureStates(controller);
            var persona = CreateOrLoadPersona();
            var desktopPetPrefab = CreatePrefabVariant(sourcePrefab, controller);
            CreateScene(desktopPetPrefab, persona);
            ApplyPlayerSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = desktopPetPrefab;
            Debug.Log("Desktop Pet prototype created at " + ScenePath);
        }

        [MenuItem("Tools/Desktop Pet/构建 Windows 桌宠", false, 11)]
        public static void BuildWindowsPrototype()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                CreateOrRefreshPrototype();
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                {
                    throw new BuildFailedException("Desktop Pet scene generation was cancelled or failed.");
                }
            }

            ApplyPlayerSettings();
            Directory.CreateDirectory(BuildFolder);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = BuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new BuildFailedException("Desktop Pet build failed: " + report.summary.result);
            }

            Debug.Log("Desktop Pet build completed: " + Path.GetFullPath(BuildPath));
        }

        private static AnimatorController CreateAnimatorController()
        {
            var idleClip = LoadClip(IdleClipPath);
            var dragStartClip = LoadClip(DragStartClipPath);
            var dragLoopClip = LoadClip(DragLoopClipPath);
            var sitEnterClip = LoadClip(SitEnterClipPath);
            var sitLoopClip = LoadClip(SitLoopClipPath);

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(AnimatorPath);
            }

            EnsureBoolParameter(controller, "IsDragging");
            EnsureBoolParameter(controller, "IsSitting");

            var stateMachine = controller.layers[0].stateMachine;
            foreach (var childState in stateMachine.states)
            {
                stateMachine.RemoveState(childState.state);
            }

            var idle = stateMachine.AddState("Idle", new Vector3(250f, 50f));
            var dragStart = stateMachine.AddState("DragStart", new Vector3(500f, 0f));
            var dragLoop = stateMachine.AddState("DragLoop", new Vector3(750f, 0f));
            var sitEnter = stateMachine.AddState("SitEnter", new Vector3(500f, 180f));
            var sitLoop = stateMachine.AddState("SitLoop", new Vector3(750f, 180f));

            idle.motion = idleClip;
            dragStart.motion = dragStartClip;
            dragLoop.motion = dragLoopClip;
            sitEnter.motion = sitEnterClip;
            sitLoop.motion = sitLoopClip;
            stateMachine.defaultState = idle;

            var beginDrag = idle.AddTransition(dragStart);
            beginDrag.hasExitTime = false;
            beginDrag.duration = 0.12f;
            beginDrag.AddCondition(AnimatorConditionMode.If, 0f, "IsDragging");

            var finishDragStart = dragStart.AddTransition(dragLoop);
            finishDragStart.hasExitTime = true;
            finishDragStart.exitTime = 1f;
            finishDragStart.hasFixedDuration = true;
            finishDragStart.duration = 0.05f;

            var cancelDragStart = dragStart.AddTransition(idle);
            cancelDragStart.hasExitTime = false;
            cancelDragStart.duration = 0.12f;
            cancelDragStart.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDragging");
            cancelDragStart.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsSitting");

            var attachDuringDragStart = dragStart.AddTransition(sitEnter);
            attachDuringDragStart.hasExitTime = false;
            attachDuringDragStart.duration = 0.12f;
            attachDuringDragStart.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDragging");
            attachDuringDragStart.AddCondition(AnimatorConditionMode.If, 0f, "IsSitting");

            var finishDrag = dragLoop.AddTransition(idle);
            finishDrag.hasExitTime = false;
            finishDrag.duration = 0.15f;
            finishDrag.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDragging");
            finishDrag.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsSitting");

            var attachAfterDrag = dragLoop.AddTransition(sitEnter);
            attachAfterDrag.hasExitTime = false;
            attachAfterDrag.duration = 0.12f;
            attachAfterDrag.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDragging");
            attachAfterDrag.AddCondition(AnimatorConditionMode.If, 0f, "IsSitting");

            var attachFromIdle = idle.AddTransition(sitEnter);
            attachFromIdle.hasExitTime = false;
            attachFromIdle.duration = 0.15f;
            attachFromIdle.AddCondition(AnimatorConditionMode.If, 0f, "IsSitting");

            var finishSitEnter = sitEnter.AddTransition(sitLoop);
            finishSitEnter.hasExitTime = true;
            finishSitEnter.exitTime = 0.92f;
            finishSitEnter.duration = 0.1f;

            var dragFromSitEnter = sitEnter.AddTransition(dragStart);
            dragFromSitEnter.hasExitTime = false;
            dragFromSitEnter.duration = 0.12f;
            dragFromSitEnter.AddCondition(AnimatorConditionMode.If, 0f, "IsDragging");

            var dragFromSitLoop = sitLoop.AddTransition(dragStart);
            dragFromSitLoop.hasExitTime = false;
            dragFromSitLoop.duration = 0.12f;
            dragFromSitLoop.AddCondition(AnimatorConditionMode.If, 0f, "IsDragging");

            var detachFromSit = sitLoop.AddTransition(idle);
            detachFromSit.hasExitTime = false;
            detachFromSit.duration = 0.15f;
            detachFromSit.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsSitting");
            detachFromSit.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDragging");

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(stateMachine);
            return controller;
        }

        private static void CreateOrRefreshDragAnimations(GameObject sourcePrefab)
        {
            DesktopPetDragAuthoring.Generate(sourcePrefab);
        }

        private static GameObject CreatePrefabVariant(GameObject sourcePrefab, RuntimeAnimatorController controller)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                try
                {
                    ConfigurePet(contents, controller);
                    PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }

                return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
            instance.name = "DesktopMita";
            ConfigurePet(instance, controller);

            var result = PrefabUtility.SaveAsPrefabAssetAndConnect(instance, PrefabPath, InteractionMode.AutomatedAction);
            Object.DestroyImmediate(instance);
            return result;
        }

        private static void ConfigurePet(GameObject instance, RuntimeAnimatorController controller)
        {
            var animator = instance.GetComponent<Animator>();
            if (animator == null)
            {
                animator = instance.AddComponent<Animator>();
            }

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var collider = instance.GetComponent<CapsuleCollider>();
            if (collider == null)
            {
                collider = instance.AddComponent<CapsuleCollider>();
            }

            FitColliderToRenderers(instance, collider);
            if (instance.GetComponent<PetDragController>() == null)
            {
                instance.AddComponent<PetDragController>();
            }

            // The clip owns limb articulation; only the hip-pivot pendulum is procedural.
            var procedural = instance.GetComponent<ProceduralDragPoseController>();
            if (procedural == null) procedural = instance.AddComponent<ProceduralDragPoseController>();
            procedural.enabled = true;
            if (instance.GetComponent<PetMouseLookController>() == null)
                instance.AddComponent<PetMouseLookController>();
            if (instance.GetComponent<PetSleepyEyesController>() == null)
                instance.AddComponent<PetSleepyEyesController>();
            if (instance.GetComponent<PetClickReactionController>() == null)
                instance.AddComponent<PetClickReactionController>();
        }

        private static void CreateScene(GameObject desktopPetPrefab, DesktopPetPersona persona)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var runtime = new GameObject("DesktopPetRuntime");
            runtime.AddComponent<DesktopWindowController>();
            runtime.AddComponent<DesktopHitTestController>();
            runtime.AddComponent<DesktopWindowAnchorController>();

            var pet = (GameObject)PrefabUtility.InstantiatePrefab(desktopPetPrefab, scene);
            pet.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var bounds = CalculateRendererBounds(pet);
            CreateCamera(bounds);
            CreateLight();
            CreateChatUi(runtime, persona);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.72f, 0.72f, 0.72f, 1f);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void CreateCamera(Bounds bounds)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(1f, bounds.extents.y * 1.15f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100f;

            var distance = Mathf.Max(5f, bounds.size.z + 5f);
            cameraObject.transform.position = bounds.center + Vector3.forward * distance;
            cameraObject.transform.LookAt(bounds.center, Vector3.up);
        }

        private static void CreateLight()
        {
            var lightObject = new GameObject("Key Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(35f, 145f, 0f);
        }

        private static void CreateChatUi(GameObject runtime, DesktopPetPersona persona)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(UiFontPath);
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var canvasObject = new GameObject("Chat Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(520f, 700f);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = CreateUiObject("Chat Bubble", canvasObject.transform);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.anchoredPosition = new Vector2(0f, -12f);
            panelRect.sizeDelta = new Vector2(-24f, 170f);
            var panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.06f, 0.075f, 0.1f, 0.88f);

            var responseObject = CreateUiObject("Response", panel.transform);
            var responseRect = responseObject.GetComponent<RectTransform>();
            responseRect.anchorMin = new Vector2(0f, 0f);
            responseRect.anchorMax = new Vector2(1f, 1f);
            responseRect.offsetMin = new Vector2(14f, 58f);
            responseRect.offsetMax = new Vector2(-14f, -12f);
            var responseText = responseObject.AddComponent<Text>();
            responseText.font = font;
            responseText.fontSize = 17;
            responseText.alignment = TextAnchor.UpperLeft;
            responseText.color = new Color(0.94f, 0.95f, 1f, 1f);
            responseText.text = "你好，我还在适应桌面。";
            responseText.raycastTarget = false;

            var inputObject = CreateUiObject("Input", panel.transform);
            var inputRect = inputObject.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0f, 0f);
            inputRect.anchorMax = new Vector2(1f, 0f);
            inputRect.pivot = new Vector2(0.5f, 0f);
            inputRect.anchoredPosition = new Vector2(-43f, 10f);
            inputRect.sizeDelta = new Vector2(-114f, 38f);
            var inputImage = inputObject.AddComponent<Image>();
            inputImage.color = new Color(1f, 1f, 1f, 0.96f);
            var inputField = inputObject.AddComponent<InputField>();

            var inputTextObject = CreateUiObject("Text", inputObject.transform);
            var inputTextRect = inputTextObject.GetComponent<RectTransform>();
            inputTextRect.anchorMin = Vector2.zero;
            inputTextRect.anchorMax = Vector2.one;
            inputTextRect.offsetMin = new Vector2(10f, 6f);
            inputTextRect.offsetMax = new Vector2(-10f, -6f);
            var inputText = inputTextObject.AddComponent<Text>();
            inputText.font = font;
            inputText.fontSize = 16;
            inputText.color = new Color(0.08f, 0.09f, 0.12f, 1f);
            inputText.supportRichText = false;

            var placeholderObject = CreateUiObject("Placeholder", inputObject.transform);
            var placeholderRect = placeholderObject.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(10f, 6f);
            placeholderRect.offsetMax = new Vector2(-10f, -6f);
            var placeholder = placeholderObject.AddComponent<Text>();
            placeholder.font = font;
            placeholder.fontSize = 16;
            placeholder.fontStyle = FontStyle.Italic;
            placeholder.color = new Color(0.4f, 0.42f, 0.48f, 0.8f);
            placeholder.text = "和我说点什么…";

            inputField.textComponent = inputText;
            inputField.placeholder = placeholder;
            inputField.lineType = InputField.LineType.SingleLine;

            var buttonObject = CreateUiObject("Send", panel.transform);
            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0f);
            buttonRect.anchorMax = new Vector2(1f, 0f);
            buttonRect.pivot = new Vector2(1f, 0f);
            buttonRect.anchoredPosition = new Vector2(-10f, 10f);
            buttonRect.sizeDelta = new Vector2(76f, 38f);
            var buttonImage = buttonObject.AddComponent<Image>();
            buttonImage.color = new Color(0.25f, 0.48f, 0.95f, 1f);
            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;

            var buttonLabelObject = CreateUiObject("Label", buttonObject.transform);
            var buttonLabelRect = buttonLabelObject.GetComponent<RectTransform>();
            buttonLabelRect.anchorMin = Vector2.zero;
            buttonLabelRect.anchorMax = Vector2.one;
            buttonLabelRect.offsetMin = Vector2.zero;
            buttonLabelRect.offsetMax = Vector2.zero;
            var buttonLabel = buttonLabelObject.AddComponent<Text>();
            buttonLabel.font = font;
            buttonLabel.fontSize = 16;
            buttonLabel.fontStyle = FontStyle.Bold;
            buttonLabel.alignment = TextAnchor.MiddleCenter;
            buttonLabel.color = Color.white;
            buttonLabel.text = "发送";
            buttonLabel.raycastTarget = false;

            var backend = runtime.AddComponent<CodexSubscriptionChatBackend>();
            backend.ConfigurePersona(persona);
            runtime.AddComponent<GPTSoVitsSpeechController>();
            var chatController = runtime.AddComponent<DesktopChatController>();
            chatController.Configure(inputField, responseText, button, backend);
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            var result = new GameObject(name, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            return result;
        }

        private static void FitColliderToRenderers(GameObject root, CapsuleCollider collider)
        {
            var bounds = CalculateRendererBounds(root);
            var localCenter = root.transform.InverseTransformPoint(bounds.center);
            var scale = root.transform.lossyScale;
            var safeScaleX = Mathf.Max(0.0001f, Mathf.Abs(scale.x));
            var safeScaleY = Mathf.Max(0.0001f, Mathf.Abs(scale.y));
            var safeScaleZ = Mathf.Max(0.0001f, Mathf.Abs(scale.z));

            collider.direction = 1;
            collider.center = localCenter;
            collider.height = bounds.size.y / safeScaleY;
            collider.radius = Mathf.Max(bounds.size.x / safeScaleX, bounds.size.z / safeScaleZ) * 0.35f;
        }

        private static Bounds CalculateRendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position + Vector3.up, new Vector3(1f, 2f, 1f));
            }

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static AnimationClip LoadClip(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                throw new FileNotFoundException("Desktop Pet animation clip was not found.", path);
            }

            return clip;
        }

        private static DesktopPetPersona CreateOrLoadPersona()
        {
            var persona = AssetDatabase.LoadAssetAtPath<DesktopPetPersona>(PersonaPath);
            if (persona != null)
            {
                return persona;
            }

            persona = ScriptableObject.CreateInstance<DesktopPetPersona>();
            AssetDatabase.CreateAsset(persona, PersonaPath);
            return persona;
        }

        private static void EnsureBoolParameter(AnimatorController controller, string parameterName)
        {
            foreach (var parameter in controller.parameters)
            {
                if (parameter.name == parameterName)
                {
                    return;
                }
            }

            controller.AddParameter(parameterName, AnimatorControllerParameterType.Bool);
        }

        private static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            var current = parts[0];
            for (var index = 1; index < parts.Length; index++)
            {
                var next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }

                current = next;
            }
        }

        private static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "DesktopPetPrototype";
            PlayerSettings.productName = "Desktop Mita Prototype";
            PlayerSettings.defaultScreenWidth = 520;
            PlayerSettings.defaultScreenHeight = 700;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = false;
            PlayerSettings.allowFullscreenSwitch = false;
            PlayerSettings.runInBackground = true;
            // WS_EX_LAYERED/LWA_COLORKEY transparency needs the legacy D3D11
            // BitBlt presentation path. The DXGI flip-model swapchain presents
            // as an opaque rectangle on Windows.
            PlayerSettings.useFlipModelSwapchain = false;
        }
    }
}
