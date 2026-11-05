using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Dodgeball.EditorTools
{
    /// <summary>
    /// Editor helper that does the whole scene setup with one click:
    /// Tools > Dodgeball > 1. Fix Pink Materials (URP)
    /// Tools > Dodgeball > 2. Build Dodgeball Scene
    /// Everything it creates is normal GameObjects/components you can tweak afterwards.
    /// </summary>
    public static class DodgeballSceneBuilder
    {
        private const string Root = "Assets/Dodgeball";
        private const string ScenePath = Root + "/Scenes/Dodgeball.unity";
        private const string DemoScenePath = "Assets/MarpaStudio/DemoScene/DemoScene.unity";
        private const string UrpPackagePath = "Assets/MarpaStudio/URP/ArenaURP.unitypackage";
        private const string BallModelPath = "Assets/Saritasa/Models/Sport_Balls/Volleyball.prefab";

        // Object name prefixes in the MarpaStudio arena that belong to the basketball hoops.
        private static readonly string[] HoopPrefixes = { "Ring", "Net", "FoamPoleFinal", "FoamFinal", "BasketBallRack", "BasketBall" };

        private const float BallDiameter = 0.6f;   // meters (a real dodgeball is ~0.2 m; bigger reads better on screen)
        private const float CourtInset = 0.95f;    // use 95 % of the PlayField mesh as playable court

        // ---------------------------------------------------------------- materials

        [MenuItem("Tools/Dodgeball/1. Fix Pink Materials (URP)")]
        public static void FixMaterials()
        {
            if (File.Exists(UrpPackagePath))
            {
                // The arena ships with a URP version of all its materials - import it silently.
                AssetDatabase.ImportPackage(UrpPackagePath, false);
                Debug.Log("[Dodgeball] Importing MarpaStudio URP materials...");
            }
            int converted = ConvertStandardMaterials("Assets/Saritasa");
            if (!File.Exists(UrpPackagePath)) converted += ConvertStandardMaterials("Assets/MarpaStudio");
            Debug.Log($"[Dodgeball] Converted {converted} Standard materials to URP/Lit.");
        }

        private static int ConvertStandardMaterials(string folder)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                Material m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || m.shader == null || !m.shader.name.StartsWith("Standard")) continue;

                Texture main = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                Color color = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
                Texture normal = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
                Texture metal = m.HasProperty("_MetallicGlossMap") ? m.GetTexture("_MetallicGlossMap") : null;
                Texture occl = m.HasProperty("_OcclusionMap") ? m.GetTexture("_OcclusionMap") : null;

                m.shader = lit;
                m.SetTexture("_BaseMap", main);
                m.SetColor("_BaseColor", color);
                if (normal != null) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
                if (metal != null) { m.SetTexture("_MetallicGlossMap", metal); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
                if (occl != null) { m.SetTexture("_OcclusionMap", occl); m.EnableKeyword("_OCCLUSIONMAP"); }
                EditorUtility.SetDirty(m);
                count++;
            }
            AssetDatabase.SaveAssets();
            return count;
        }

        // ---------------------------------------------------------------- scene

        [MenuItem("Tools/Dodgeball/2. Build Dodgeball Scene")]
        public static void BuildScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Dodgeball", "Dodgeball scene already exists. Rebuild it from scratch?", "Rebuild", "Cancel"))
                return;

            EnsureFolder(Root + "/Scenes"); EnsureFolder(Root + "/Prefabs"); EnsureFolder(Root + "/Materials");
            int characterLayer = EnsureLayer("Character");
            int ballLayer = EnsureLayer("Ball");
            int dividerLayer = EnsureLayer("Divider");
            Physics.IgnoreLayerCollision(ballLayer, dividerLayer, true);

            // 1) Copy the demo arena into our own scene and open it.
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.CopyAsset(DemoScenePath, ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // 2) Remove the basketball hoops (disabling also disables their colliders).
            var allObjects = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
            var disabled = new List<string>();
            foreach (Transform t in allObjects)
            {
                if (t == null) continue;
                if (HoopPrefixes.Any(p => t.name.StartsWith(p)) && t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(false);
                    disabled.Add(t.name);
                }
            }
            Debug.Log("[Dodgeball] Disabled hoop objects: " + string.Join(", ", disabled));

            // 3) Measure the court from the PlayField mesh.
            Transform playField = allObjects.FirstOrDefault(t => t != null && t.name.StartsWith("PlayField"));
            Bounds court = new Bounds(Vector3.zero, new Vector3(15f, 0f, 28f));
            if (playField != null)
            {
                Renderer[] rs = playField.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    court = rs[0].bounds;
                    foreach (Renderer r in rs) court.Encapsulate(r.bounds);
                }
            }
            else Debug.LogWarning("[Dodgeball] PlayField not found - using a default 28x15 court at the origin.");

            bool longIsX = court.size.x > court.size.z;
            Vector3 lengthAxis = longIsX ? Vector3.right : Vector3.forward;
            float halfLength = (longIsX ? court.size.x : court.size.z) * 0.5f * CourtInset;
            float halfWidth = (longIsX ? court.size.z : court.size.x) * 0.5f * CourtInset;
            Vector3 center = new Vector3(court.center.x, court.max.y, court.center.z);
            Debug.Log($"[Dodgeball] Court center {center}, size {court.size}, long axis {(longIsX ? "X" : "Z")}");

            // 4) Gameplay root + arena bounds.
            GameObject root = new GameObject("Dodgeball");
            GameObject arenaGo = new GameObject("ArenaBounds");
            arenaGo.transform.SetParent(root.transform);
            arenaGo.transform.SetPositionAndRotation(center, Quaternion.LookRotation(lengthAxis, Vector3.up));
            ArenaBounds arena = arenaGo.AddComponent<ArenaBounds>();
            arena.Configure(halfLength, halfWidth);

            // 5) Invisible colliders: floor, 4 walls around the court, center divider (characters only).
            Transform colliders = new GameObject("Colliders").transform;
            colliders.SetParent(arenaGo.transform, false);
            const float wallHeight = 8f, wallThickness = 1f, wallGap = 1.5f;
            float outerL = halfLength + wallGap, outerW = halfWidth + wallGap;
            AddBox(colliders, "Floor", new Vector3(0, -0.5f, 0), new Vector3(outerW * 2 + 10, 1f, outerL * 2 + 10), 0);
            AddBox(colliders, "Wall_PlayerEnd", new Vector3(0, wallHeight / 2, -outerL - wallThickness / 2), new Vector3(outerW * 2 + 2, wallHeight, wallThickness), 0);
            AddBox(colliders, "Wall_AIEnd", new Vector3(0, wallHeight / 2, outerL + wallThickness / 2), new Vector3(outerW * 2 + 2, wallHeight, wallThickness), 0);
            AddBox(colliders, "Wall_Left", new Vector3(-outerW - wallThickness / 2, wallHeight / 2, 0), new Vector3(wallThickness, wallHeight, outerL * 2 + 2), 0);
            AddBox(colliders, "Wall_Right", new Vector3(outerW + wallThickness / 2, wallHeight / 2, 0), new Vector3(wallThickness, wallHeight, outerL * 2 + 2), 0);
            AddBox(colliders, "CenterDivider", new Vector3(0, 1.5f, 0), new Vector3(outerW * 2, 3f, 0.2f), dividerLayer);

            // 6) Ball prefab.
            Ball ballPrefab = CreateBallPrefab(ballLayer);

            // 7) Characters.
            Material blue = CreateMaterial("PlayerMat", new Color(0.2f, 0.45f, 1f));
            Material red = CreateMaterial("AIMat", new Color(1f, 0.3f, 0.25f));
            GameObject player = CreateCharacter("Player", -1, blue, characterLayer, root.transform, arena);
            GameObject ai = CreateCharacter("AI", 1, red, characterLayer, root.transform, arena);
            player.tag = "Player";
            PlayerController pc = player.AddComponent<PlayerController>();
            AIController aic = ai.AddComponent<AIController>();
            Set(aic, "target", player.transform);

            // 8) Camera behind the player's half, looking down at the court.
            Camera cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
                cam.gameObject.AddComponent<AudioListener>();
            }
            cam.transform.SetParent(null);
            cam.transform.position = center - lengthAxis * (halfLength + 7f) + Vector3.up * 15f;
            cam.transform.LookAt(center - lengthAxis * 2f);
            cam.fieldOfView = 55f;
            if (cam.GetComponent<CameraShake>() == null) cam.gameObject.AddComponent<CameraShake>();
            Set(pc, "aimCamera", cam);

            // 9) Game manager.
            GameManager gm = new GameObject("GameManager").AddComponent<GameManager>();
            gm.transform.SetParent(root.transform);
            Set(gm, "player", player.GetComponent<Health>());
            Set(gm, "opponent", ai.GetComponent<Health>());
            Set(gm, "ballPrefab", ballPrefab);

            // 10) UI.
            BuildUI(root.transform, player, ai);

            // 11) Save + add to Build Settings as the first scene (needed for Restart).
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath && File.Exists(s.path)).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Selection.activeGameObject = root;
            Debug.Log("[Dodgeball] Scene built and saved to " + ScenePath + ". Press Play!");
        }

        // ---------------------------------------------------------------- character models

        /// <summary>
        /// Select a character model (FBX or prefab) in the Project window, then run this.
        /// Select two models (Ctrl+click) to give the Player the first and the AI the second.
        /// The capsule stays as the physics body; only its look is replaced.
        /// </summary>
        [MenuItem("Tools/Dodgeball/3. Use Selected Model(s) For Characters")]
        public static void UseSelectedModels()
        {
            GameObject[] models = Selection.GetFiltered<GameObject>(SelectionMode.Assets)
                .Where(g => AssetDatabase.Contains(g)).ToArray();
            if (models.Length == 0)
            {
                EditorUtility.DisplayDialog("Dodgeball", "Select a character model (FBX or prefab) in the Project window first.", "OK");
                return;
            }

            GameObject player = GameObject.Find("Dodgeball/Player");
            GameObject ai = GameObject.Find("Dodgeball/AI");
            if (player == null || ai == null)
            {
                EditorUtility.DisplayDialog("Dodgeball", "Open the Dodgeball scene first (Tools > Dodgeball > 2. Build Dodgeball Scene).", "OK");
                return;
            }

            ApplyModel(player, models[0], new Color(0.2f, 0.45f, 1f));
            ApplyModel(ai, models[models.Length > 1 ? 1 : 0], new Color(1f, 0.3f, 0.25f));
            EditorSceneManager.MarkSceneDirty(player.scene);
            EditorSceneManager.SaveScene(player.scene);
            Debug.Log($"[Dodgeball] Characters now use '{models[0].name}'" + (models.Length > 1 ? $" and '{models[1].name}'." : "."));
        }

        [MenuItem("Tools/Dodgeball/4. Revert Characters To Capsules")]
        public static void RevertToCapsules()
        {
            foreach (string n in new[] { "Dodgeball/Player", "Dodgeball/AI" })
            {
                GameObject go = GameObject.Find(n);
                if (go == null) continue;
                Transform old = go.transform.Find("Model");
                if (old != null) Object.DestroyImmediate(old.gameObject);
                Transform ring = go.transform.Find("TeamRing");
                if (ring != null) Object.DestroyImmediate(ring.gameObject);
                go.GetComponent<MeshRenderer>().enabled = true;
                Transform nose = go.transform.Find("FacingIndicator");
                if (nose != null) nose.gameObject.SetActive(true);
                EditorSceneManager.MarkSceneDirty(go.scene);
            }
        }

        private static void ApplyModel(GameObject character, GameObject modelAsset, Color teamColor)
        {
            // Remove a previous model / ring.
            Transform old = character.transform.Find("Model");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Transform oldRing = character.transform.Find("TeamRing");
            if (oldRing != null) Object.DestroyImmediate(oldRing.gameObject);

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, character.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            // The model must not fight our CharacterController / physics.
            foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (Rigidbody rb in model.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
            foreach (MonoBehaviour mb in model.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
            foreach (CharacterController cc in model.GetComponentsInChildren<CharacterController>(true)) cc.enabled = false;

            // Scale to the capsule height (2 m) and put the feet on the capsule bottom (-1 m local).
            const float targetHeight = 1.9f;
            Bounds b = RendererBounds(model);
            if (b.size.y > 0.001f)
            {
                float worldScaleY = character.transform.lossyScale.y;
                model.transform.localScale = Vector3.one * (targetHeight / (b.size.y / worldScaleY));
                b = RendererBounds(model);
                float feetLocalY = character.transform.InverseTransformPoint(new Vector3(0, b.min.y, 0)).y;
                model.transform.localPosition = new Vector3(0f, -1f - feetLocalY, 0f);
            }
            SetLayerRecursive(model, character.layer);

            // Hide capsule look.
            character.GetComponent<MeshRenderer>().enabled = false;
            Transform nose = character.transform.Find("FacingIndicator");
            if (nose != null) nose.gameObject.SetActive(false);

            // Colored ring under the feet so you can tell the teams apart.
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "TeamRing";
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(character.transform, false);
            ring.transform.localPosition = new Vector3(0f, -0.98f, 0f);
            ring.transform.localScale = new Vector3(1.2f, 0.01f, 1.2f);
            Material ringMat = CreateMaterial(teamColor.r > teamColor.b ? "AIRing" : "PlayerRing", teamColor);
            ring.GetComponent<Renderer>().sharedMaterial = ringMat;
            SetLayerRecursive(ring, character.layer);

            // Animator driving + hit flash on the new renderers.
            CharacterVisual visual = character.GetComponent<CharacterVisual>();
            if (visual == null) visual = character.AddComponent<CharacterVisual>();
            Set(visual, "animator", model.GetComponentInChildren<Animator>());
            Animator anim = model.GetComponentInChildren<Animator>();
            if (anim != null && anim.runtimeAnimatorController == null)
                Debug.LogWarning($"[Dodgeball] '{modelAsset.name}' has an Animator without a controller - assign one (e.g. with Idle/Walk and a 'Speed' float) to animate it.");
        }

        // ---------------------------------------------------------------- pieces

        private static Ball CreateBallPrefab(int ballLayer)
        {
            PhysicsMaterial bouncy = new PhysicsMaterial("DodgeballPhysics")
            {
                bounciness = 0.55f,
                dynamicFriction = 0.5f,
                staticFriction = 0.6f,
                bounceCombine = PhysicsMaterialCombine.Maximum,
                frictionCombine = PhysicsMaterialCombine.Average
            };
            string matPath = Root + "/Materials/DodgeballPhysics.asset";
            AssetDatabase.DeleteAsset(matPath);
            AssetDatabase.CreateAsset(bouncy, matPath);

            GameObject root = new GameObject("Dodgeball");
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(BallModelPath);
            if (source != null)
            {
                GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(source);
                PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (Collider c in visual.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                foreach (Rigidbody rb in visual.GetComponentsInChildren<Rigidbody>()) Object.DestroyImmediate(rb);
                visual.name = "Model";
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = Vector3.zero;

                // Scale the model so its biggest dimension equals BallDiameter, then center it.
                Bounds b = RendererBounds(visual);
                float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
                if (size > 0.0001f) visual.transform.localScale *= BallDiameter / size;
                b = RendererBounds(visual);
                visual.transform.localPosition -= b.center - root.transform.position;
            }
            else
            {
                Debug.LogWarning("[Dodgeball] Ball model not found, using a sphere.");
                GameObject s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(s.GetComponent<Collider>());
                s.transform.SetParent(root.transform, false);
                s.transform.localScale = Vector3.one * BallDiameter;
            }

            SetLayerRecursive(root, ballLayer);
            SphereCollider sphere = root.AddComponent<SphereCollider>();
            sphere.radius = BallDiameter * 0.5f;
            sphere.sharedMaterial = bouncy;
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = 0.4f;
            body.linearDamping = 0.1f;
            body.angularDamping = 0.5f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            root.AddComponent<Ball>();

            string prefabPath = Root + "/Prefabs/Dodgeball.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<Ball>();
        }

        private static GameObject CreateCharacter(string name, int side, Material mat, int layer, Transform parent, ArenaBounds arena)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<CapsuleCollider>()); // CharacterController is the collider
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.transform.SetParent(parent);
            go.transform.position = arena.SpawnPoint(side) + Vector3.up * 1.1f;
            go.transform.rotation = Quaternion.LookRotation(arena.LengthAxis * -side);

            // A small "nose" so you can see where the character faces.
            GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "FacingIndicator";
            Object.DestroyImmediate(nose.GetComponent<Collider>());
            nose.GetComponent<Renderer>().sharedMaterial = mat;
            nose.transform.SetParent(go.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.5f, 0.45f);
            nose.transform.localScale = new Vector3(0.3f, 0.15f, 0.3f);

            Transform hold = new GameObject("HoldPoint").transform;
            hold.SetParent(go.transform, false);
            hold.localPosition = new Vector3(0.55f, 0.3f, 0.45f);

            SetLayerRecursive(go, layer);
            CharacterController cc = go.AddComponent<CharacterController>();
            cc.height = 2f; cc.radius = 0.5f; cc.center = Vector3.zero;

            go.AddComponent<Health>();
            CharacterMotor motor = go.AddComponent<CharacterMotor>();
            Set(motor, "side", side);
            go.AddComponent<DodgeAbility>();
            BallThrower thrower = go.AddComponent<BallThrower>();
            Set(thrower, "holdPoint", hold);
            go.AddComponent<HitFeedback>();
            return go;
        }

        private static void BuildUI(Transform parent, GameObject player, GameObject ai)
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            Sprite box = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                GameObject es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(parent);
            }

            GameObject canvasGo = new GameObject("UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(parent);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            RectTransform c = canvasGo.GetComponent<RectTransform>();

            Text playerLives = MakeText(c, "PlayerLives", font, 56, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(40, -30), new Vector2(600, 90));
            playerLives.color = new Color(0.45f, 0.65f, 1f);
            Text aiLives = MakeText(c, "AILives", font, 56, TextAnchor.UpperRight, new Vector2(1, 1), new Vector2(-40, -30), new Vector2(600, 90));
            aiLives.color = new Color(1f, 0.45f, 0.4f);

            // Dodge cooldown: dark circle + radial fill + label.
            Image dodgeBg = MakeImage(c, "DodgeBackground", knob, new Color(0, 0, 0, 0.5f), new Vector2(0, 0), new Vector2(110, 110), new Vector2(150, 150));
            Image dodgeFill = MakeImage(dodgeBg.rectTransform, "DodgeFill", knob, Color.green, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(140, 140));
            dodgeFill.type = Image.Type.Filled;
            dodgeFill.fillMethod = Image.FillMethod.Radial360;
            dodgeFill.fillOrigin = (int)Image.Origin360.Top;
            dodgeFill.fillClockwise = true;
            Text dodgeLabel = MakeText(dodgeBg.rectTransform, "DodgeLabel", font, 26, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150, 50));
            dodgeLabel.color = Color.black;
            MakeText(dodgeBg.rectTransform, "DodgeKey", font, 22, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0, -20), new Vector2(200, 30)).text = "[Space]";

            // Throw charge bar.
            Image chargeBg = MakeImage(c, "ChargeBackground", box, new Color(0, 0, 0, 0.5f), new Vector2(0.5f, 0), new Vector2(0, 60), new Vector2(400, 24));
            Image chargeFill = MakeImage(chargeBg.rectTransform, "ChargeFill", box, new Color(1f, 0.75f, 0.2f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(396, 20));
            chargeFill.type = Image.Type.Filled;
            chargeFill.fillMethod = Image.FillMethod.Horizontal;
            chargeFill.fillAmount = 0f;

            MakeText(c, "Help", font, 22, TextAnchor.LowerRight, new Vector2(1, 0), new Vector2(-30, 30), new Vector2(800, 120)).text =
                "WASD move  |  Space dodge  |  E pick up\nHold & release Left Mouse to throw";

            // Game over panel.
            Image panel = MakeImage(c, "GameOverPanel", null, new Color(0, 0, 0, 0.7f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            panel.rectTransform.anchorMin = Vector2.zero; panel.rectTransform.anchorMax = Vector2.one;
            panel.rectTransform.sizeDelta = Vector2.zero;
            Text goText = MakeText(panel.rectTransform, "Result", font, 120, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 80), new Vector2(1200, 200));
            goText.fontStyle = FontStyle.Bold;
            Image btnImg = MakeImage(panel.rectTransform, "RestartButton", box, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(320, 90));
            btnImg.type = Image.Type.Sliced;
            Button btn = btnImg.gameObject.AddComponent<Button>();
            Text btnText = MakeText(btnImg.rectTransform, "Label", font, 44, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320, 90));
            btnText.text = "Restart"; btnText.color = Color.black;

            UIManager ui = canvasGo.AddComponent<UIManager>();
            Set(ui, "player", player.GetComponent<Health>());
            Set(ui, "opponent", ai.GetComponent<Health>());
            Set(ui, "playerDodge", player.GetComponent<DodgeAbility>());
            Set(ui, "playerController", player.GetComponent<PlayerController>());
            Set(ui, "playerLivesText", playerLives);
            Set(ui, "opponentLivesText", aiLives);
            Set(ui, "dodgeCooldownFill", dodgeFill);
            Set(ui, "dodgeLabel", dodgeLabel);
            Set(ui, "chargeFill", chargeFill);
            Set(ui, "gameOverPanel", panel.gameObject);
            Set(ui, "gameOverText", goText);
            Set(ui, "restartButton", btn);

            AddDifficultyMenu(ui);
        }

        // ---------------------------------------------------------------- difficulty menu

        [MenuItem("Tools/Dodgeball/5. Add Difficulty Menu To Open Scene")]
        public static void AddDifficultyMenuToOpenScene()
        {
            UIManager ui = Object.FindFirstObjectByType<UIManager>(FindObjectsInactive.Include);
            if (ui == null)
            {
                EditorUtility.DisplayDialog("Dodgeball", "Open the Dodgeball scene first.", "OK");
                return;
            }
            AddDifficultyMenu(ui);
            EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
            EditorSceneManager.SaveScene(ui.gameObject.scene);
            Debug.Log("[Dodgeball] Difficulty menu added. Press Play to choose Easy / Normal / Hard.");
        }

        /// <summary>Creates the Easy/Normal/Hard start panel, a "Menu" button on the game over panel and a HUD label.</summary>
        private static void AddDifficultyMenu(UIManager ui)
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Sprite box = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            RectTransform canvas = ui.GetComponent<RectTransform>();

            // Remove what a previous run created, so this can be run again safely.
            foreach (string n in new[] { "DifficultyPanel", "DifficultyLabel" })
            {
                Transform old = canvas.Find(n);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }
            Transform gameOver = canvas.Find("GameOverPanel");
            if (gameOver != null)
            {
                Transform oldMenu = gameOver.Find("MenuButton");
                if (oldMenu != null) Object.DestroyImmediate(oldMenu.gameObject);
            }

            // HUD label under the player's lives.
            Text label = MakeText(canvas, "DifficultyLabel", font, 28, TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(400, 50));
            label.text = "";

            // Full-screen start panel (drawn last so it covers the HUD).
            Image panel = MakeImage(canvas, "DifficultyPanel", null, new Color(0f, 0f, 0f, 0.75f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            panel.rectTransform.anchorMin = Vector2.zero; panel.rectTransform.anchorMax = Vector2.one;
            panel.rectTransform.sizeDelta = Vector2.zero;
            panel.transform.SetAsLastSibling();

            Text title = MakeText(panel.rectTransform, "Title", font, 130, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 300), new Vector2(1400, 180));
            title.text = "DODGEBALL"; title.fontStyle = FontStyle.Bold;
            MakeText(panel.rectTransform, "Subtitle", font, 46, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 170), new Vector2(1200, 70)).text = "Choose difficulty";

            Button easy = MakeMenuButton(panel.rectTransform, "EasyButton", "EASY", "Relaxed - easy to beat", new Color(0.35f, 0.8f, 0.4f), new Vector2(-460, -10), font, box);
            Button normal = MakeMenuButton(panel.rectTransform, "NormalButton", "NORMAL", "A real challenge", new Color(1f, 0.78f, 0.25f), new Vector2(0, -10), font, box);
            Button hard = MakeMenuButton(panel.rectTransform, "HardButton", "HARD", "Fast, accurate, dodges a lot", new Color(1f, 0.35f, 0.3f), new Vector2(460, -10), font, box);

            MakeText(panel.rectTransform, "Controls", font, 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, -260), new Vector2(1400, 100)).text =
                "WASD move  |  Space dodge  |  E pick up  |  Hold & release Left Mouse to throw";

            // "Menu" button next to Restart on the game over screen.
            Button menu = null;
            if (gameOver != null)
            {
                Transform restart = gameOver.Find("RestartButton");
                if (restart != null) ((RectTransform)restart).anchoredPosition = new Vector2(-180, -90);
                Image mImg = MakeImage((RectTransform)gameOver, "MenuButton", box, Color.white, new Vector2(0.5f, 0.5f), new Vector2(180, -90), new Vector2(320, 90));
                mImg.type = Image.Type.Sliced;
                menu = mImg.gameObject.AddComponent<Button>();
                Text mText = MakeText(mImg.rectTransform, "Label", font, 44, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320, 90));
                mText.text = "Menu"; mText.color = Color.black;
            }

            Set(ui, "difficultyPanel", panel.gameObject);
            Set(ui, "easyButton", easy);
            Set(ui, "normalButton", normal);
            Set(ui, "hardButton", hard);
            Set(ui, "difficultyText", label);
            if (menu != null) Set(ui, "menuButton", menu);
        }

        private static Button MakeMenuButton(RectTransform parent, string name, string title, string description, Color color, Vector2 pos, Font font, Sprite box)
        {
            Image img = MakeImage(parent, name, box, color, new Vector2(0.5f, 0.5f), pos, new Vector2(400, 200));
            img.type = Image.Type.Sliced;
            Button b = img.gameObject.AddComponent<Button>();
            ColorBlock cb = b.colors;
            cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            b.colors = cb;
            Text t = MakeText(img.rectTransform, "Title", font, 64, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(400, 90));
            t.text = title; t.fontStyle = FontStyle.Bold; t.color = Color.black;
            Text d = MakeText(img.rectTransform, "Description", font, 26, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, -45), new Vector2(380, 50));
            d.text = description; d.color = new Color(0.1f, 0.1f, 0.1f);
            return b;
        }

        // ---------------------------------------------------------------- helpers

        private static Text MakeText(RectTransform parent, string name, Font font, int size, TextAnchor align, Vector2 anchor, Vector2 pos, Vector2 dim)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = dim;
            Text t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = align; t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.raycastTarget = false;
            go.AddComponent<Shadow>().effectDistance = new Vector2(2, -2);
            return t;
        }

        private static Image MakeImage(RectTransform parent, string name, Sprite sprite, Color color, Vector2 anchor, Vector2 pos, Vector2 dim)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = dim;
            Image img = go.GetComponent<Image>();
            img.sprite = sprite; img.color = color;
            return img;
        }

        private static void AddBox(Transform parent, string name, Vector3 localPos, Vector3 size, int layer)
        {
            GameObject go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<BoxCollider>().size = size;
        }

        private static Material CreateMaterial(string name, Color color)
        {
            string path = $"{Root}/Materials/{name}.mat";
            AssetDatabase.DeleteAsset(path);
            Material m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static Bounds RendererBounds(GameObject go)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        /// <summary>Sets a private [SerializeField] through SerializedObject (same as dragging in the Inspector).</summary>
        private static void Set(Object target, string field, object value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[Dodgeball] Field '{field}' not found on {target.GetType().Name}"); return; }
            switch (value)
            {
                case int i: p.intValue = i; break;
                case Object o: p.objectReferenceValue = o; break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static int EnsureLayer(string layerName)
        {
            int existing = LayerMask.NameToLayer(layerName);
            if (existing >= 0) return existing;

            SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty sp = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(sp.stringValue))
                {
                    sp.stringValue = layerName;
                    tagManager.ApplyModifiedPropertiesWithoutUndo();
                    return i;
                }
            }
            Debug.LogError("[Dodgeball] No free layer slot for " + layerName);
            return 0;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
