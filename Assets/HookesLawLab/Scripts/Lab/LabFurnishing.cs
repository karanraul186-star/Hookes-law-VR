using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace HookesLawLab.Lab
{
    /// <summary>
    /// Builds the procedural side benches, chairs, ceiling cyan LED strips, architectural lighting,
    /// room obstacle colliders, and XR TeleportationArea matching Anam_sir_WebXR/furnishing.js & collisionSystem.js.
    /// </summary>
    public class LabFurnishing : MonoBehaviour
    {
        public void BuildEnvironmentAndCollisions(GameObject anamRoot, float xSign)
        {
            BuildSideBenchesAndChairs(xSign);
            UpdateLabWallAndFloorMaterials(anamRoot);
            SetupLighting();
            BuildCollisionObstacles(xSign);
            BuildFloorTeleportArea();
        }

        private void BuildSideBenchesAndChairs(float xSign)
        {
            GameObject root = new GameObject("SideBenchesAndChairs");
            root.transform.SetParent(transform, false);

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

            Material greyMat = new Material(litShader) { color = HexColor(0x7a8794) };
            greyMat.SetFloat("_Smoothness", 0.35f);

            Material topMat = new Material(litShader) { color = HexColor(0x121b24) };
            topMat.SetFloat("_Metallic", 0.25f);
            topMat.SetFloat("_Smoothness", 0.5f);

            Material steelMat = new Material(litShader) { color = HexColor(0x8c99a3) };
            steelMat.SetFloat("_Metallic", 0.8f);
            steelMat.SetFloat("_Smoothness", 0.65f);

            Material seatMat = new Material(litShader) { color = HexColor(0x070a0e) };
            Material cyanMat = new Material(unlitShader) { color = HexColor(0x00ccee) };

            void AddPart(string partName, float x, float y, float z, float w, float h, float d, Material mat)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = partName;
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(x * xSign, y, z);
                go.transform.localScale = new Vector3(w, h, d);
                MeshRenderer mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            foreach (int side in new[] { -1, 1 })
            {
                float x = side * 3.65f;
                const float z = 7.435f;
                AddPart("Dark worktop", x, 0.9f, z, 2.8f, 0.08f, 0.75f, topMat);
                AddPart("Cyan bench trim", x, 0.85f, z - 0.38f, 2.76f, 0.018f, 0.015f, cyanMat);

                foreach (int i in new[] { -1, 1 })
                {
                    AddPart("Cabinet", x + i * 0.98f, 0.43f, z, 0.72f, 0.82f, 0.66f, greyMat);
                    AddPart("Cabinet door", x + i * 0.98f, 0.46f, z - 0.338f, 0.66f, 0.69f, 0.018f, greyMat);
                    AddPart("Steel handle", x + i * 0.98f, 0.7f, z - 0.36f, 0.24f, 0.025f, 0.035f, steelMat);
                    AddPart("Plinth", x + i * 0.98f, 0.045f, z, 0.68f, 0.09f, 0.6f, topMat);
                }

                foreach (int c in new[] { -1, 1 })
                {
                    float cx = x + c * 0.72f;
                    const float cz = 6.435f;
                    AddPart("Padded chair seat", cx, 0.57f, cz, 0.43f, 0.065f, 0.43f, seatMat);
                    AddPart("Chair backrest", cx, 0.85f, cz - 0.19f, 0.43f, 0.32f, 0.055f, seatMat);
                    AddPart("Back support", cx, 0.7f, cz - 0.18f, 0.035f, 0.3f, 0.035f, steelMat);
                    foreach (int a in new[] { -1, 1 })
                    {
                        foreach (int b in new[] { -1, 1 })
                        {
                            AddPart("Chair leg", cx + a * 0.16f, 0.28f, cz + b * 0.16f, 0.028f, 0.56f, 0.028f, steelMat);
                        }
                    }
                    AddPart("Footrest", cx, 0.2f, cz + 0.16f, 0.35f, 0.025f, 0.025f, steelMat);
                }
            }

            AddPart("Ceiling cyan left", -5.82f, 3.55f, 2.8f, 0.025f, 0.025f, 9.4f, cyanMat);
            AddPart("Ceiling cyan right", 5.82f, 3.55f, 2.8f, 0.025f, 0.025f, 9.4f, cyanMat);
            AddPart("Ceiling cyan rear", 0f, 3.55f, 7.65f, 11.6f, 0.025f, 0.025f, cyanMat);
        }

        private void UpdateLabWallAndFloorMaterials(GameObject anamRoot)
        {
            if (anamRoot == null) return;
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material floorMat = new Material(litShader) { color = HexColor(0x616e7a) };
            floorMat.SetFloat("_Smoothness", 0.3f);
            Material wallMat = new Material(litShader) { color = HexColor(0x99a6b3) };
            wallMat.SetFloat("_Smoothness", 0.3f);

            foreach (MeshRenderer mr in anamRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                string n = mr.gameObject.name.ToLowerInvariant().Replace('_', ' ');
                if (n == "room floor")
                {
                    mr.sharedMaterial = floorMat;
                }
                else if (n == "back wall" || n == "vr front wall" || n == "vr left wall full" || n == "vr right wall" || n.StartsWith("side wall inset"))
                {
                    mr.sharedMaterial = wallMat;
                }
                else
                {
                    foreach (Material m in mr.sharedMaterials)
                    {
                        if (m == null) continue;
                        string mn = m.name.ToLowerInvariant();
                        if (mn.Contains("neon") || mn.Contains("blue strip") || mn.Contains("cyan"))
                        {
                            m.EnableKeyword("_EMISSION");
                            if (m.HasProperty("_EmissionColor"))
                            {
                                m.SetColor("_EmissionColor", HexColor(0x00d4ff) * 0.7f);
                            }
                        }
                    }
                }
            }
        }

        private void SetupLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.white * 0.9f;
            RenderSettings.ambientEquatorColor = new Color(0.85f, 0.92f, 1f) * 0.75f;
            RenderSettings.ambientGroundColor = HexColor(0x1e293b) * 0.6f;

            // Reuse existing Directional Light if present, otherwise create one
            Light dirLight = FindFirstObjectByType<Light>();
            if (dirLight == null || dirLight.type != LightType.Directional)
            {
                GameObject dl = new GameObject("OverheadKeyLight");
                dl.transform.SetParent(transform, false);
                dirLight = dl.AddComponent<Light>();
                dirLight.type = LightType.Directional;
            }
            dirLight.color = Color.white;
            dirLight.intensity = 1.15f;
            dirLight.shadows = LightShadows.None;
            dirLight.transform.position = new Vector3(1.5f, 3.2f, 1.8f);
            dirLight.transform.LookAt(new Vector3(0f, 1.0f, 0f));

            // Counter fill light
            GameObject fillGo = new GameObject("CounterFillLight");
            fillGo.transform.SetParent(transform, false);
            Light fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = HexColor(0xdbeafe);
            fill.intensity = 0.6f;
            fill.shadows = LightShadows.None;
            fillGo.transform.position = new Vector3(-2.2f, 3.2f, -1.0f);
            fillGo.transform.LookAt(new Vector3(0f, 1.0f, 0f));

            // Cyan point accents
            CreatePointLight("CyanAccent1", new Vector3(0f, 2.4f, 0f), HexColor(0x00e5ff), 1.5f, 10f);
            CreatePointLight("CyanAccent2", new Vector3(0f, 1.8f, 3.8f), HexColor(0x00d4ff), 1.0f, 8f);
        }

        private void CreatePointLight(string lightName, Vector3 pos, Color col, float intensity, float range)
        {
            GameObject go = new GameObject(lightName);
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = col;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
        }

        private void BuildCollisionObstacles(float xSign)
        {
            GameObject colRoot = new GameObject("RoomAndFurnitureColliders");
            colRoot.transform.SetParent(transform, false);
            int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRaycastLayer < 0) ignoreRaycastLayer = 2;

            void AddBox(string id, float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
            {
                float ax = minX * xSign;
                float bx = maxX * xSign;
                float cx = (ax + bx) * 0.5f;
                float cy = (minY + maxY) * 0.5f;
                float cz = (minZ + maxZ) * 0.5f;
                Vector3 size = new Vector3(Mathf.Abs(bx - ax), Mathf.Abs(maxY - minY), Mathf.Abs(maxZ - minZ));

                GameObject go = new GameObject(id);
                go.layer = ignoreRaycastLayer;
                go.transform.SetParent(colRoot.transform, false);
                go.transform.position = new Vector3(cx, cy, cz);
                BoxCollider bc = go.AddComponent<BoxCollider>();
                bc.size = size;
            }

            // Room perimeter walls
            AddBox("Wall_Left", -6.3f, -5.85f, 0f, 3.8f, -2.5f, 8.2f);
            AddBox("Wall_Right", 5.85f, 6.3f, 0f, 3.8f, -2.5f, 8.2f);
            AddBox("Wall_Back", -6.2f, 6.2f, 0f, 3.8f, -2.4f, -1.95f);
            AddBox("Wall_Front", -6.2f, 6.2f, 0f, 3.8f, 7.75f, 8.2f);

            // Solid lab equipment & furniture (table collider kept to tabletop height 1.11m so tabletop UI is never occluded)
            AddBox("MainExperimentTable", -1.88f, 1.88f, 0f, 1.11f, -0.85f, 0.85f);
            AddBox("RoomFloorCollider", -6.2f, 6.2f, -0.10f, 0.0f, -2.5f, 8.2f);
            AddBox("StandColumnLeft", -1.12f, -1.00f, 1.11f, 3.25f, -0.30f, -0.18f);
            AddBox("StandColumnRight", 0.38f, 0.50f, 1.11f, 3.25f, -0.30f, -0.18f);
            AddBox("StandTopCrossbar", -1.12f, 0.50f, 3.20f, 3.30f, -0.30f, -0.18f);
            AddBox("TeacherAvatarCollider", -3.60f, -2.80f, 0f, 2.0f, -0.32f, 0.46f);
            AddBox("SideEquipmentBench", 4.30f, 6.00f, 0f, 2.8f, 2.15f, 5.95f);
            AddBox("LeftWallWorkShelf", -5.90f, -5.20f, 0f, 1.11f, 0.40f, 2.45f);
        }

        private void BuildFloorTeleportArea()
        {
            GameObject floorTp = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floorTp.name = "TeleportationFloor";
            floorTp.transform.SetParent(transform, false);
            floorTp.transform.position = new Vector3(0f, 0.002f, 2.9f);
            floorTp.transform.localScale = new Vector3(1.16f, 1f, 0.96f); // ~11.6m x 9.6m playable floor

            MeshRenderer mr = floorTp.GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false; // Invisible collider surface matching existing Anam floor

            TeleportationArea tpArea = floorTp.AddComponent<TeleportationArea>();
            tpArea.matchDirectionalInput = false;
        }

        private static Color HexColor(int hex)
        {
            float r = ((hex >> 16) & 0xFF) / 255f;
            float g = ((hex >> 8) & 0xFF) / 255f;
            float b = (hex & 0xFF) / 255f;
            return new Color(r, g, b, 1f);
        }
    }
}
