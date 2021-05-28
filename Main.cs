using System;
using System.Linq;
using Harmony;

namespace ml_clv
{
    public class CalibrationLinesVisualizer : MelonLoader.MelonMod
    {
        public static readonly string[] gc_trackerTypes =
        {
            "waist", "left_foot", "right_foot",
            "left_elbow", "right_elbow",
            "left_knee", "right_knee",
            "chest"
        };
        public static readonly UnityEngine.HumanBodyBones[] gc_linkedBones =
        {
            UnityEngine.HumanBodyBones.Hips, UnityEngine.HumanBodyBones.LeftFoot, UnityEngine.HumanBodyBones.RightFoot,
            UnityEngine.HumanBodyBones.LeftLowerArm, UnityEngine.HumanBodyBones.RightLowerArm,
            UnityEngine.HumanBodyBones.LeftLowerLeg, UnityEngine.HumanBodyBones.RightLowerLeg,
            UnityEngine.HumanBodyBones.Chest,
        };

        static CalibrationLinesVisualizer ms_instance = null;

        bool m_enabled = true;
        bool m_calibrating = false;
        int m_maxTrackersCount = 3;

        UnityEngine.Color m_lineColor = UnityEngine.Color.green;
        UnityEngine.Material m_lineMaterial = null;
        System.Collections.Generic.List<UnityEngine.LineRenderer> m_lines = null;

        SteamVR_ControllerManager m_controllerManager = null;

        public override void OnApplicationStart()
        {
            ms_instance = this;

            MelonLoader.MelonPreferences.CreateCategory("CLV", "Calibration Lines Visualizer");
            MelonLoader.MelonPreferences.CreateEntry("CLV", "Enabled", true, "Enable lines for trackers");
            MelonLoader.MelonPreferences.CreateEntry("CLV", "LineColorR", 0f, "Red color component for lines");
            MelonLoader.MelonPreferences.CreateEntry("CLV", "LineColorG", 1f, "Green color component for lines");
            MelonLoader.MelonPreferences.CreateEntry("CLV", "LineColorB", 0f, "Blue color component for lines");

            // VRCTrackingManager.PrepareForCalibration()
            System.Reflection.MethodInfo l_pfcMethod = null;
            l_pfcMethod = typeof(VRCTrackingManager).GetMethods()
                .Where(m => (m.Name.StartsWith("Method_Public_Static_Void_") && m.ReturnType == typeof(void) && m.GetParameters().Count() == 0 && UnhollowerRuntimeLib.XrefScans.XrefScanner.XrefScan(m)
                .Where(x => (x.Type == UnhollowerRuntimeLib.XrefScans.XrefType.Global && x.ReadAsObject().ToString().Contains("trying to calibrate"))).Any())).First();
            if(l_pfcMethod != null)
            {
                Harmony.Patch(l_pfcMethod, null, new Harmony.HarmonyMethod(typeof(CalibrationLinesVisualizer), nameof(Prefix_VRCTrackingManager_PrepareForCalibration)));

                // VRCTracking.RestoreTrackingAfterCalibration()
                System.Reflection.MethodInfo l_rtacMethod = null;
                l_rtacMethod = typeof(VRCTrackingManager).GetMethods()
                        .Where(m => (m.Name.StartsWith("Method_Public_Static_Void_") && m.Name != l_pfcMethod.Name && m.ReturnType == typeof(void) && m.GetParameters().Count() == 0 && UnhollowerRuntimeLib.XrefScans.XrefScanner.UsedBy(m)
                        .Where(x => (x.Type == UnhollowerRuntimeLib.XrefScans.XrefType.Method && x.TryResolve()?.DeclaringType == typeof(VRCFbbIkController))).Any())).First();
                if(l_rtacMethod != null)
                {
                    Harmony.Patch(l_rtacMethod, null, new Harmony.HarmonyMethod(typeof(CalibrationLinesVisualizer), nameof(Prefix_VRCTrackingManager_RestoreTrackingAfterCalibration)));
                }
                else
                    MelonLoader.MelonLogger.Warning("VRCTrackingManager.RestoreTrackingAfterCalibration patch failed");
            }
            else
                MelonLoader.MelonLogger.Warning("VRCTrackingManager.PrepareForCalibration patch failed");

            // IKTweaks search
            foreach(var l_mod in MelonLoader.MelonHandler.Mods)
            {
                if(l_mod.Info.Name == "IKTweaks")
                {
                    MelonLoader.MelonLogger.Msg("IKTweaks detected");

                    Type l_cbType = null;
                    l_mod.Assembly.GetTypes().DoIf(t => t.Name == "CalibrationManager", t => l_cbType = t);
                    if(l_cbType != null)
                    {
                        m_maxTrackersCount = 8;

                        Harmony.Patch(l_cbType.GetMethod("Calibrate"), null, new Harmony.HarmonyMethod(typeof(CalibrationLinesVisualizer), nameof(Prefix_VRCTrackingManager_PrepareForCalibration)));
                        Harmony.Patch(l_cbType.GetMethod("ApplyStoredCalibration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static), null, new Harmony.HarmonyMethod(typeof(CalibrationLinesVisualizer), nameof(Prefix_VRCTrackingManager_RestoreTrackingAfterCalibration)));

                        MelonLoader.MelonLogger.Msg("IKTweaks hooked");
                    }

                    break;
                }
            }

            m_lines = new System.Collections.Generic.List<UnityEngine.LineRenderer>();

            OnPreferencesSaved();
        }

        public override void OnPreferencesSaved()
        {
            m_enabled = MelonLoader.MelonPreferences.GetEntryValue<bool>("CLV", "Enabled");
            m_lineColor.r = MelonLoader.MelonPreferences.GetEntryValue<float>("CLV", "LineColorR");
            m_lineColor.g = MelonLoader.MelonPreferences.GetEntryValue<float>("CLV", "LineColorG");
            m_lineColor.b = MelonLoader.MelonPreferences.GetEntryValue<float>("CLV", "LineColorB");

            if(m_lineMaterial != null)
                m_lineMaterial.color = m_lineColor;
        }

        public override void OnUpdate()
        {
            if(m_enabled && m_calibrating && (m_controllerManager != null))
            {
                var l_animator = VRCPlayer.field_Internal_Static_VRCPlayer_0?.field_Private_VRC_AnimationController_0?.field_Private_Animator_0;
                bool l_humanoid = ((l_animator != null) ? l_animator.isHuman : false);

                for(int i = 0; i < m_maxTrackersCount; i++)
                {
                    var l_puck = m_controllerManager.field_Public_ArrayOf_GameObject_0[i + 2];
                    if(l_puck.active)
                    {
                        UnityEngine.Vector3? l_start = null;
                        if((l_animator != null) && l_humanoid)
                            l_start = l_animator.GetBoneTransform(FindBoneForDevice(m_controllerManager.field_Private_ArrayOf_UInt32_0[i + 2], l_animator, l_puck.transform))?.position;
                        if(l_start == null)
                            l_start = l_puck.transform.position;

                        m_lines[i].SetPosition(0, (UnityEngine.Vector3)l_start);
                        m_lines[i].SetPosition(1, l_puck.transform.position);
                        m_lines[i].widthMultiplier = 0.025f * VRCTrackingManager.field_Private_Static_VRCTrackingManager_0.gameObject.transform.localScale.x;
                    }
                }
            }
        }

        static public void Prefix_VRCTrackingManager_PrepareForCalibration() => ms_instance?.OnPrepareForCalibration();
        void OnPrepareForCalibration()
        {
            m_calibrating = true;
            foreach(var l_line in m_lines)
                l_line.gameObject.active = true;
        }

        static public void Prefix_VRCTrackingManager_RestoreTrackingAfterCalibration() => ms_instance?.OnRestoreTrackingAfterCalibration();
        void OnRestoreTrackingAfterCalibration()
        {
            m_calibrating = false;
            foreach(var l_line in m_lines)
                l_line.gameObject.active = false;
        }

        [Harmony.HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.OnJoinedRoom))]
        class Patch_NetworkManager_OnJoinedRoom
        {
            static void Postfix() => ms_instance.OnJoinedRoom();
        }
        void OnJoinedRoom()
        {
            // It's here for the right reasons
            if(m_controllerManager == null)
            {
                FindControllerManager();
                if(m_controllerManager != null)
                {
                    if(m_lineMaterial == null)
                    {
                        m_lineMaterial = new UnityEngine.Material(UnityEngine.Shader.Find("Unlit/Color"));
                        m_lineMaterial.color = m_lineColor;
                    }

                    for(int i = 0; i < m_maxTrackersCount; i++)
                    {
                        var l_line = new UnityEngine.GameObject("CLV_Line" + i).AddComponent<UnityEngine.LineRenderer>();
                        UnityEngine.Object.DontDestroyOnLoad(l_line.gameObject);
                        l_line.gameObject.active = false;

                        l_line.material = m_lineMaterial;
                        l_line.alignment = UnityEngine.LineAlignment.View;
                        l_line.castShadows = false;
                        l_line.widthMultiplier = 0.025f;
                        l_line.positionCount = 2;
                        for(int j = 0; j < l_line.positionCount; j++)
                            l_line.SetPosition(j, UnityEngine.Vector3.zeroVector);

                        l_line.transform.parent = m_controllerManager.field_Public_ArrayOf_GameObject_0[i + 2].transform;

                        m_lines.Add(l_line);
                    }
                }
            }
        }

        void FindControllerManager()
        {
            if(VRCInputManager.field_Private_Static_Dictionary_2_EnumNPublicSealedvaKeMoCoGaViOcViDaWaUnique_VRCInputProcessor_0.Count == 0)
                return;
            VRCInputProcessor l_input = VRCInputManager.field_Private_Static_Dictionary_2_EnumNPublicSealedvaKeMoCoGaViOcViDaWaUnique_VRCInputProcessor_0[VRCInputManager.EnumNPublicSealedvaKeMoCoGaViOcViDaWaUnique.Vive];
            if(l_input)
            {
                VRCInputProcessorVive l_castInput = l_input.TryCast<VRCInputProcessorVive>();
                if(l_castInput)
                {
                    m_controllerManager = l_castInput.field_Private_SteamVR_ControllerManager_0;
                    return;
                }
            }

            l_input = VRCInputManager.field_Private_Static_Dictionary_2_EnumNPublicSealedvaKeMoCoGaViOcViDaWaUnique_VRCInputProcessor_0[VRCInputManager.EnumNPublicSealedvaKeMoCoGaViOcViDaWaUnique.ViveAdvanced];
            if(l_input)
            {
                VRCInputProcessorViveAdvanced l_castInput = l_input.TryCast<VRCInputProcessorViveAdvanced>();
                if(l_castInput)
                {
                    m_controllerManager = l_castInput.field_Private_SteamVR_ControllerManager_0;
                    return;
                }
            }

            l_input = VRCInputManager.field_Private_Static_Dictionary_2_EnumNPublicSealedvaKeMoCoGaViOcViDaWaUnique_VRCInputProcessor_0[VRCInputManager.EnumNPublicSealedvaKeMoCoGaViOcViDaWaUnique.Index];
            if(l_input)
            {
                VRCInputProcessorIndex l_castInput = l_input.TryCast<VRCInputProcessorIndex>();
                if(l_castInput)
                {
                    m_controllerManager = l_castInput.field_Private_SteamVR_ControllerManager_0;
                    return;
                }
            }
        }

        UnityEngine.HumanBodyBones FindBoneForDevice(uint f_deviceId, UnityEngine.Animator f_animator, UnityEngine.Transform f_puck)
        {
            UnityEngine.HumanBodyBones l_result = UnityEngine.HumanBodyBones.LastBone;

            var l_stringBuilder = new Il2CppSystem.Text.StringBuilder(64);
            Valve.VR.ETrackedPropertyError l_error = Valve.VR.ETrackedPropertyError.TrackedProp_NotYetAvailable;
            Valve.VR.OpenVR.System.GetStringTrackedDeviceProperty(f_deviceId, Valve.VR.ETrackedDeviceProperty.Prop_ControllerType_String, l_stringBuilder, (uint)l_stringBuilder.Capacity, ref l_error);
            if(l_error == Valve.VR.ETrackedPropertyError.TrackedProp_Success)
            {
                string l_controllerType = l_stringBuilder.ToString();
                int l_controllerTypeId = -1;
                for(int i = 0; i < m_maxTrackersCount; i++)
                {
                    if(l_controllerType.Contains(gc_trackerTypes[i]))
                    {
                        l_controllerTypeId = i;
                        break;
                    }
                }

                if(l_controllerTypeId != -1)
                    l_result = gc_linkedBones[l_controllerTypeId];
            }

            // Tracker is unassigned in SteamVR or has no right property 
            if(l_result == UnityEngine.HumanBodyBones.LastBone)
            {
                // Find nearest bone
                float l_distance = float.MaxValue;
                foreach(var l_bone in gc_linkedBones)
                {
                    var l_boneTransform = f_animator.GetBoneTransform(l_bone);
                    if(l_boneTransform != null)
                    {
                        float l_distanceToPuck = UnityEngine.Vector3.Distance(l_boneTransform.position, f_puck.position);
                        if(l_distanceToPuck < l_distance)
                        {
                            l_distance = l_distanceToPuck;
                            l_result = l_bone;
                        }
                    }
                }
            }

            // No bone, revert to hips
            if(l_result == UnityEngine.HumanBodyBones.LastBone)
                l_result = UnityEngine.HumanBodyBones.Hips;
            return l_result;
        }
    }
}
