//-----------------------------------------------------------------------------------------
// ToggleHUD.cs - A small draggable desktop HUD with four on/off toggles,
// an on-panel scale bar, DPI-aware scaling, and saveable default settings.
//
//   * Soft Body   -> UserPreferences.singleton.softPhysics (global, via reflection)
//   * GiveMeFPS   -> the "enabled" bool on every Redeyes.GiveMeFPS storable
//   * Adv Coll    -> GiveMeFPS "Disable All Advanced Colliders" bool
//   * High Phys   -> UserPreferences.singleton.physicsHighQuality (Settings > Physics > High Quality Physics, via reflection)
//
// Pattern (world-space canvas parented to Camera.main, registered via
// SuperController.AddCanvas) is modelled on VamEssentials.DockedUI.
// v4: VR mode. In VR the HUD is a free-floating world-space panel (not head-locked):
// drag it by its header/body with the laser, BRING it back, PIN it to your view, attach it to a
// controller, keep it on top, and its scale + placement are remembered across scenes.
// Desktop behaviour (camera-parented, mouse-drag) is unchanged.
// Written as a runtime-compiled MVRScript - no enum declarations, no
// $-string interpolation (both trip VaM's Mono compiler).
//-----------------------------------------------------------------------------------------
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using System.Reflection;
using System.Collections.Generic;
using MeshVR;
using MVR.FileManagementSecure;
using SimpleJSON;

namespace zerot
{
    public class ToggleHUD : MVRScript
    {
        // ---- persisted layout ----
        private JSONStorableFloat posX;      // camera-local metres, right(+)
        private JSONStorableFloat posY;      // camera-local metres, up(+)
        private JSONStorableFloat uiScale;   // panel scale multiplier
        private JSONStorableBool  dpiAware;  // compensate for screen DPI
        private JSONStorableBool  showHud;   // master visibility

        // ---- persisted toggle state ----
        private JSONStorableBool  softState; // true = soft body on
        private JSONStorableBool  gtState;   // true = GiveMeFPS enabled
        private JSONStorableBool  acState;   // true = advanced colliders enabled
        private JSONStorableBool  hpState;   // true = High Quality Physics on

        // ---- runtime objects ----
        private Canvas canvas;
        private GameObject panelGO;
        private float canvasDistance = 1.0f; // metres in front of camera
        private Font uiFont;

        private Image softBtnImg, gtBtnImg, acBtnImg, hpBtnImg;
        private Text  softTxt, gtTxt, acTxt, hpTxt;
        private Image softDot, gtDot, acDot, hpDot;
        private float lastHpPoll;

        private ScaleBar scaleBar;
        private Text scaleReadout;

        // ---- penis length feature (gazed male atom) ----
        private ScaleBar lengthBar;
        private Text lengthReadout;
        private Text lengthTitle;
        private Atom gazedMale;
        private DAZMorph penisMorph;
        private float lastGazeScan;

        // ---- VR mode ----
        private bool vrMode;
        private JSONStorableBool pinToView;
        private JSONStorableBool alwaysOnTop;
        private JSONStorableBool rememberVR;
        private JSONStorableStringChooser attachTo;
        private Image pinBtnImg;
        private Text  pinBtnTxt;
        private Material normalDepthMaterial;
        private Vector3 pinnedLocalPos;
        private Quaternion pinnedLocalRot = Quaternion.identity;
        private bool pinCaptured;
        private bool haveVRLayout;
        private Vector3 vrLocalPos;
        private Quaternion vrLocalRot = Quaternion.identity;
        private float vrScale;
        private float vrEnforceUntil;
        private float vrSaveDueAt = -1f;
        private string vrSig = "";
        private const string VR_PATH = "Saves/PluginData/zerot/ToggleHUD/vr_layout.json";

        private const string DEF_DIR  = "Saves/PluginData/zerot/ToggleHUD";
        private const string DEF_PATH = "Saves/PluginData/zerot/ToggleHUD/defaults.json";
        private const float SCALE_MIN = 0.4f;
        private const float SCALE_MAX = 2.5f;

        private static readonly Color ON_COL  = new Color(0.16f, 0.62f, 0.30f, 1f);
        private static readonly Color OFF_COL = new Color(0.55f, 0.17f, 0.17f, 1f);
        private static readonly Color ON_DOT  = new Color(0.45f, 1f, 0.55f, 1f);
        private static readonly Color OFF_DOT = new Color(1f, 0.45f, 0.45f, 1f);
        private static readonly Color HDR_COL = new Color(0.13f, 0.14f, 0.17f, 0.96f);
        private static readonly Color BG_COL  = new Color(0.10f, 0.11f, 0.13f, 0.90f);
        private static readonly Color BAR_BG  = new Color(0.28f, 0.30f, 0.34f, 1f);
        private static readonly Color BAR_FILL= new Color(0.36f, 0.58f, 0.90f, 1f);
        private static readonly Color BAR_HND = new Color(0.85f, 0.88f, 0.95f, 1f);

        // ------------------------------------------------------------------
        public override void Init()
        {
            try
            {
                uiFont = GetFont();

                posX     = new JSONStorableFloat("HUD Pos X", -0.30f, -1.2f, 1.2f, false, true);
                posY     = new JSONStorableFloat("HUD Pos Y",  0.18f, -1.0f, 1.0f, false, true);
                uiScale  = new JSONStorableFloat("HUD Scale",  1.0f, SCALE_MIN, SCALE_MAX, true, true);
                dpiAware = new JSONStorableBool("DPI Aware", true);
                showHud  = new JSONStorableBool("Show HUD", true);
                pinToView   = new JSONStorableBool("Pin To View (VR)", false);
                alwaysOnTop = new JSONStorableBool("Always On Top (VR)", true);
                rememberVR  = new JSONStorableBool("Remember VR Layout", true);
                attachTo    = new JSONStorableStringChooser("Attach HUD To (VR)", new List<string> { "None", "Left Hand", "Right Hand" }, "None", "Attach HUD To (VR)");

                uiScale.setCallbackFunction  = delegate(float v) { ApplyTransform(); if (scaleBar != null) scaleBar.SetFromValue(v); UpdateScaleReadout(); };
                dpiAware.setCallbackFunction = delegate(bool v)  { ApplyTransform(); };
                showHud.setCallbackFunction  = delegate(bool v)  { if (panelGO != null) panelGO.SetActive(v); };

                softState = new JSONStorableBool("Soft Body On", ReadSoft());
                gtState   = new JSONStorableBool("GiveMeFPS Enabled", true);
                acState   = new JSONStorableBool("Advanced Colliders Enabled", true);
                hpState   = new JSONStorableBool("High Physics On", ReadHigh());
                pinToView.setCallbackFunction   = delegate(bool v) { OnPinChanged(v); };
                alwaysOnTop.setCallbackFunction = delegate(bool v) { ApplyDepth(v); };
                rememberVR.setCallbackFunction  = delegate(bool v) { if (v) LoadVRLayout(true); };
                attachTo.setCallbackFunction    = delegate(string v) { OnAttachChanged(v); };
                softState.setCallbackFunction = delegate(bool v) { RepaintAll(); };
                gtState.setCallbackFunction   = delegate(bool v) { RepaintAll(); };
                acState.setCallbackFunction   = delegate(bool v) { RepaintAll(); };
                hpState.setCallbackFunction   = delegate(bool v) { RepaintAll(); };

                RegisterFloat(posX);
                RegisterFloat(posY);
                RegisterFloat(uiScale);
                RegisterBool(dpiAware);
                RegisterBool(showHud);
                RegisterBool(pinToView);
                RegisterBool(alwaysOnTop);
                RegisterBool(rememberVR);
                RegisterStringChooser(attachTo);
                RegisterBool(softState);
                RegisterBool(gtState);
                RegisterBool(acState);
                RegisterBool(hpState);

                // plugin panel controls
                CreateSlider(uiScale, false);
                CreateToggle(dpiAware, false);
                CreateToggle(showHud, false);
                CreateToggle(pinToView, false);
                CreateToggle(alwaysOnTop, false);
                CreateToggle(rememberVR, false);
                CreatePopup(attachTo, false);
                CreateButton("Reset HUD Position", false).button.onClick.AddListener(delegate {
                    posX.val = -0.30f; posY.val = 0.18f;
                    if (vrMode) BringHud(); else ApplyTransform();
                });
                CreateButton("Save Current As Default", true).button.onClick.AddListener(delegate { SaveDefaults(); });
                CreateButton("Load Default", true).button.onClick.AddListener(delegate { LoadDefaults(); RepaintAll(); ApplyTransform(); });

                // pull in saved defaults (scene-stored values still override these,
                // because VaM restores them after Init)
                LoadDefaults();

                BuildHud();
                RepaintAll();
            }
            catch (Exception e)
            {
                SuperController.LogError("ToggleHUD Init: " + e);
            }
        }

        // ------------------------------------------------------------------
        // HUD construction
        // ------------------------------------------------------------------
        private void BuildHud()
        {
            GameObject canvasGO = new GameObject("ToggleHUD_Canvas");
            canvasGO.transform.SetParent(transform, false);

            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.pixelPerfect = false;
            SuperController.singleton.AddCanvas(canvas);

            CanvasScaler cs = canvasGO.AddComponent<CanvasScaler>();
            cs.dynamicPixelsPerUnit = 1f;
            canvasGO.AddComponent<GraphicRaycaster>();

            vrMode = IsVRMode();
            if (vrMode) canvasGO.transform.SetParent(null, false);   // free-floating world-space panel
            else canvasGO.transform.SetParent(Camera.main.transform, false);

            // root panel
            panelGO = NewUI("Panel", canvasGO.transform, 360f, 446f);
            Image bg = panelGO.AddComponent<Image>();
            bg.color = BG_COL;
            DragMove dm = panelGO.AddComponent<DragMove>();
            dm.owner = this;

            // header bar
            GameObject header = NewUI("Header", panelGO.transform, 360f, 46f);
            SetAnchoredTop(header, 0f);
            Image hi = header.AddComponent<Image>();
            hi.color = HDR_COL;
            DragMove dmh = header.AddComponent<DragMove>();
            dmh.owner = this;
            Text ht = AddText(header, "  QUICK TOGGLES", 20, TextAnchor.MiddleLeft);
            ht.color = new Color(0.85f, 0.87f, 0.92f, 1f);
            Text hh = AddText(header, "(drag)   ", 15, TextAnchor.MiddleRight);
            hh.color = new Color(0.5f, 0.53f, 0.6f, 1f);
            if (vrMode)
            {
                // BRING / PIN are panel siblings (not header children) so pressing them never starts a drag.
                ht.text = "QUICK TOGGLES";
                ht.alignment = TextAnchor.MiddleCenter;
                hh.text = "";
                Image bi;
                Text bt;
                AddHeaderButton(panelGO.transform, "BRING", -144f, out bi, out bt, delegate { BringHud(); });
                AddHeaderButton(panelGO.transform, "PIN", 144f, out pinBtnImg, out pinBtnTxt, delegate { pinToView.val = !pinToView.val; });
                RefreshPinButton();
            }

            // three toggle rows
            softBtnImg = AddRow(panelGO.transform, -66f, "SOFT BODY", out softTxt, out softDot,
                delegate { softState.val = !softState.val; ApplySoft(); RepaintAll(); });
            gtBtnImg = AddRow(panelGO.transform, -128f, "GIVEMEFPS (GT)", out gtTxt, out gtDot,
                delegate { gtState.val = !gtState.val; ApplyGt(); RepaintAll(); });
            acBtnImg = AddRow(panelGO.transform, -190f, "ADV COLLIDERS", out acTxt, out acDot,
                delegate { acState.val = !acState.val; ApplyAc(); RepaintAll(); });

            hpBtnImg = AddRow(panelGO.transform, -252f, "HIGH PHYSICS", out hpTxt, out hpDot,
                delegate { hpState.val = !hpState.val; ApplyHigh(); RepaintAll(); });

            // scale bar row
            BuildScaleRow(panelGO.transform, -314f);

            // penis-length row (drives the gazed male atom's morph)
            BuildLengthRow(panelGO.transform, -370f);

            panelGO.SetActive(showHud.val);
            if (scaleBar != null) scaleBar.SetFromValue(uiScale.val);
            UpdateScaleReadout();
            ApplyTransform();
            if (vrMode)
            {
                BringHud();
                ApplyDepth(alwaysOnTop.val);
                if (rememberVR.val) LoadVRLayout(true);
            }
        }

        private void AddHeaderButton(Transform parent, string label, float x, out Image img, out Text txt, UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = NewUI("Btn_" + label, parent, 64f, 34f);
            SetAnchoredTop(go, -6f);
            go.GetComponent<RectTransform>().anchoredPosition = new Vector2(x, -6f);
            img = go.AddComponent<Image>();
            img.color = new Color(0.20f, 0.22f, 0.28f, 1f);
            Button b = go.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(onClick);
            txt = AddText(go, label, 14, TextAnchor.MiddleCenter);
            txt.fontStyle = FontStyle.Bold;
        }

        private Image AddRow(Transform parent, float top, string label, out Text stateTxt, out Image dot, UnityEngine.Events.UnityAction onClick)
        {
            GameObject row = NewUI("Row_" + label, parent, 336f, 52f);
            SetAnchoredTop(row, top);
            Image bimg = row.AddComponent<Image>();
            bimg.color = OFF_COL;
            Button b = row.AddComponent<Button>();
            b.targetGraphic = bimg;
            b.onClick.AddListener(onClick);

            GameObject dgo = NewUI("Dot", row.transform, 22f, 22f);
            RectTransform drt = dgo.GetComponent<RectTransform>();
            drt.anchorMin = new Vector2(0f, 0.5f);
            drt.anchorMax = new Vector2(0f, 0.5f);
            drt.anchoredPosition = new Vector2(24f, 0f);
            dot = dgo.AddComponent<Image>();
            dot.color = OFF_DOT;
            dot.raycastTarget = false;

            Text lt = AddText(row, "      " + label, 19, TextAnchor.MiddleLeft);
            lt.color = Color.white;

            GameObject sgo = NewUI("State", row.transform, 90f, 40f);
            RectTransform srt = sgo.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(1f, 0.5f);
            srt.anchorMax = new Vector2(1f, 0.5f);
            srt.anchoredPosition = new Vector2(-52f, 0f);
            stateTxt = sgo.AddComponent<Text>();
            stateTxt.font = uiFont;
            stateTxt.fontSize = 20;
            stateTxt.fontStyle = FontStyle.Bold;
            stateTxt.alignment = TextAnchor.MiddleRight;
            stateTxt.text = "OFF";
            stateTxt.color = Color.white;
            stateTxt.raycastTarget = false;

            return bimg;
        }

        // custom draggable scale bar (avoids Unity Slider's internal anchor wiring)
        private void BuildScaleRow(Transform parent, float top)
        {
            GameObject row = NewUI("ScaleRow", parent, 336f, 44f);
            SetAnchoredTop(row, top);

            Text lab = AddText(row, "  SCALE", 16, TextAnchor.MiddleLeft);
            lab.color = new Color(0.7f, 0.73f, 0.8f, 1f);

            GameObject sgo = NewUI("ScaleReadout", row.transform, 80f, 40f);
            RectTransform srt = sgo.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(1f, 0.5f);
            srt.anchorMax = new Vector2(1f, 0.5f);
            srt.anchoredPosition = new Vector2(-14f, 0f);
            scaleReadout = sgo.AddComponent<Text>();
            scaleReadout.font = uiFont;
            scaleReadout.fontSize = 17;
            scaleReadout.alignment = TextAnchor.MiddleRight;
            scaleReadout.color = Color.white;
            scaleReadout.raycastTarget = false;
            scaleReadout.text = "1.0x";

            float trackWidth = 176f;
            float barH = 8f;
            GameObject track = NewUI("Track", row.transform, trackWidth, 26f);
            RectTransform trt = track.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.5f, 0.5f);
            trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = new Vector2(-2f, 0f);
            Image trackHit = track.AddComponent<Image>();
            trackHit.color = new Color(0f, 0f, 0f, 0f); // invisible but raycastable grab area
            trackHit.raycastTarget = true;

            float half = trackWidth * 0.5f;
            Image barBg   = MakeBarPart(track.transform, BAR_BG,   trackWidth, barH, half);
            Image barFill = MakeBarPart(track.transform, BAR_FILL, trackWidth * 0.5f, barH, half);

            GameObject hgo = NewUI("Handle", track.transform, 20f, 20f);
            RectTransform hrt = hgo.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0.5f, 0.5f);
            hrt.anchorMax = new Vector2(0.5f, 0.5f);
            hrt.pivot = new Vector2(0.5f, 0.5f);
            Image hImg = hgo.AddComponent<Image>();
            hImg.color = BAR_HND;
            hImg.raycastTarget = false;

            scaleBar = track.AddComponent<ScaleBar>();
            scaleBar.owner = this;
            scaleBar.track = trt;
            scaleBar.fill = barFill.GetComponent<RectTransform>();
            scaleBar.handle = hrt;
            scaleBar.trackWidth = trackWidth;
            scaleBar.minV = SCALE_MIN;
            scaleBar.maxV = SCALE_MAX;
            scaleBar.onChange = delegate(float v) { OnScaleBar(v); };
        }

        // Penis-length row: same custom bar, but drives the gazed male atom's morph.
        private void BuildLengthRow(Transform parent, float top)
        {
            GameObject row = NewUI("LengthRow", parent, 336f, 44f);
            SetAnchoredTop(row, top);

            lengthTitle = AddText(row, "  PENIS (aim at male)", 16, TextAnchor.MiddleLeft);
            lengthTitle.color = new Color(0.7f, 0.73f, 0.8f, 1f);

            GameObject sgo = NewUI("LenReadout", row.transform, 90f, 40f);
            RectTransform srt = sgo.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(1f, 0.5f);
            srt.anchorMax = new Vector2(1f, 0.5f);
            srt.anchoredPosition = new Vector2(-14f, 0f);
            lengthReadout = sgo.AddComponent<Text>();
            lengthReadout.font = uiFont;
            lengthReadout.fontSize = 17;
            lengthReadout.alignment = TextAnchor.MiddleRight;
            lengthReadout.color = Color.white;
            lengthReadout.raycastTarget = false;
            lengthReadout.text = "--";

            float trackWidth = 176f;
            float barH = 8f;
            GameObject track = NewUI("LenTrack", row.transform, trackWidth, 26f);
            RectTransform trt = track.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.5f, 0.5f);
            trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = new Vector2(-2f, 0f);
            Image trackHit = track.AddComponent<Image>();
            trackHit.color = new Color(0f, 0f, 0f, 0f);
            trackHit.raycastTarget = true;

            float half = trackWidth * 0.5f;
            MakeBarPart(track.transform, BAR_BG, trackWidth, barH, half);
            Image barFill = MakeBarPart(track.transform, BAR_FILL, trackWidth * 0.5f, barH, half);

            GameObject hgo = NewUI("LenHandle", track.transform, 20f, 20f);
            RectTransform hrt = hgo.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0.5f, 0.5f);
            hrt.anchorMax = new Vector2(0.5f, 0.5f);
            hrt.pivot = new Vector2(0.5f, 0.5f);
            Image hImg = hgo.AddComponent<Image>();
            hImg.color = BAR_HND;
            hImg.raycastTarget = false;

            lengthBar = track.AddComponent<ScaleBar>();
            lengthBar.owner = this;
            lengthBar.track = trt;
            lengthBar.fill = barFill.GetComponent<RectTransform>();
            lengthBar.handle = hrt;
            lengthBar.trackWidth = trackWidth;
            lengthBar.minV = 0f;    // 0.00
            lengthBar.maxV = 10f;   // 10.00
            lengthBar.onChange = delegate(float v) { OnLengthBar(v); };
            lengthBar.SetFromValue(0f);
        }

        // called by the length bar when dragged
        public void OnLengthBar(float v)
        {
            if (penisMorph != null)
            {
                penisMorph.morphValue = v;
                v = penisMorph.morphValue;   // read back the actually-applied value
            }
            if (lengthReadout != null) lengthReadout.text = v.ToString("0.00");
        }

        private Image MakeBarPart(Transform parent, Color col, float w, float h, float half)
        {
            GameObject go = NewUI("Bar", parent, w, h);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(-half, 0f);
            rt.sizeDelta = new Vector2(w, h);
            Image img = go.AddComponent<Image>();
            img.color = col;
            img.raycastTarget = false;
            return img;
        }

        private void UpdateScaleReadout()
        {
            if (scaleReadout != null && uiScale != null)
                scaleReadout.text = uiScale.val.ToString("0.0") + "x";
        }

        // called by the scale bar when dragged
        public void OnScaleBar(float v)
        {
            if (uiScale == null) return;
            uiScale.val = Mathf.Clamp(v, uiScale.min, uiScale.max);
        }

        // ------------------------------------------------------------------
        // Visual repaint
        // ------------------------------------------------------------------
        private void RepaintAll()
        {
            Paint(softBtnImg, softTxt, softDot, softState != null && softState.val);
            Paint(gtBtnImg,   gtTxt,   gtDot,   gtState   != null && gtState.val);
            Paint(acBtnImg,   acTxt,   acDot,   acState   != null && acState.val);
            Paint(hpBtnImg,   hpTxt,   hpDot,   hpState   != null && hpState.val);
        }

        private void Paint(Image btn, Text txt, Image dot, bool on)
        {
            if (btn != null) btn.color = on ? ON_COL : OFF_COL;
            if (dot != null) dot.color = on ? ON_DOT : OFF_DOT;
            if (txt != null) txt.text  = on ? "ON" : "OFF";
        }

        // ------------------------------------------------------------------
        // Target application
        // ------------------------------------------------------------------
        private void ApplySoft()
        {
            WriteSoft(softState.val);
        }

        // High Quality Physics (Settings > Physics): the real preference decides; the button only mirrors it
        private void ApplyHigh()
        {
            WriteHigh(hpState.val);
            // read it back: if VaM refused, the button shows the truth
            bool actual = ReadHigh();
            if (actual != hpState.val) hpState.val = actual;
        }

        private void ApplyGt()
        {
            ForEachGiveMeFPS(delegate(JSONStorable js) {
                JSONStorableBool p = js.GetBoolJSONParam("enabled");
                if (p != null) p.val = gtState.val;
            });
        }

        private void ApplyAc()
        {
            bool disable = !acState.val; // "Enabled" means colliders ON, i.e. NOT disabled
            ForEachGiveMeFPS(delegate(JSONStorable js) {
                JSONStorableBool p = js.GetBoolJSONParam("Disable All Advanced Colliders");
                if (p == null) p = js.GetBoolJSONParam("Disable All Advanced Colliders ");
                if (p != null) p.val = disable;
            });
        }

        private void ForEachGiveMeFPS(Action<JSONStorable> act)
        {
            try
            {
                List<Atom> atoms = SuperController.singleton.GetAtoms();
                for (int i = 0; i < atoms.Count; i++)
                {
                    Atom a = atoms[i];
                    List<string> ids = a.GetStorableIDs();
                    if (ids == null) continue;
                    for (int j = 0; j < ids.Count; j++)
                    {
                        if (ids[j].Contains("GiveMeFPS"))
                        {
                            JSONStorable js = a.GetStorableByID(ids[j]);
                            if (js != null) act(js);
                        }
                    }
                }
            }
            catch (Exception e) { SuperController.LogError("ToggleHUD scan: " + e.Message); }
        }

        // ------------------------------------------------------------------
        // Soft physics via reflection (member-name safe)
        // ------------------------------------------------------------------
        private object PrefsObj()
        {
            try { return UserPreferences.singleton; } catch { return null; }
        }

        private bool ReadHigh()
        {
            object p = PrefsObj();
            if (p == null) return false;
            try
            {
                System.Type t = p.GetType();
                PropertyInfo pr = t.GetProperty("physicsHighQuality");
                if (pr != null) return (bool)pr.GetValue(p, null);
                FieldInfo f = t.GetField("physicsHighQuality");
                if (f != null) return (bool)f.GetValue(p);
            }
            catch { }
            return false;
        }

        private void WriteHigh(bool v)
        {
            object p = PrefsObj();
            if (p == null) return;
            try
            {
                System.Type t = p.GetType();
                PropertyInfo pr = t.GetProperty("physicsHighQuality");
                if (pr != null) { pr.SetValue(p, v, null); return; }
                FieldInfo f = t.GetField("physicsHighQuality");
                if (f != null) f.SetValue(p, v);
            }
            catch (Exception e) { SuperController.LogError("ToggleHUD high physics: " + e.Message); }
        }

        private bool ReadSoft()
        {
            object p = PrefsObj();
            if (p == null) return false;
            try
            {
                System.Type t = p.GetType();
                FieldInfo f = t.GetField("softPhysics");
                if (f != null) return (bool)f.GetValue(p);
                PropertyInfo pr = t.GetProperty("softPhysics");
                if (pr != null) return (bool)pr.GetValue(p, null);
            }
            catch { }
            return false;
        }

        private void WriteSoft(bool v)
        {
            object p = PrefsObj();
            if (p == null) return;
            try
            {
                System.Type t = p.GetType();
                FieldInfo f = t.GetField("softPhysics");
                if (f != null) { f.SetValue(p, v); return; }
                PropertyInfo pr = t.GetProperty("softPhysics");
                if (pr != null) { pr.SetValue(p, v, null); }
            }
            catch (Exception e) { SuperController.LogError("ToggleHUD soft: " + e.Message); }
        }

        // ------------------------------------------------------------------
        // Positioning & DPI
        // ------------------------------------------------------------------
        private float DpiFactor()
        {
            if (dpiAware == null || !dpiAware.val) return 1f;
            float d = Screen.dpi;
            if (d < 1f) return 1f;
            return Mathf.Clamp(d / 96f, 0.5f, 3f);
        }

        public void ApplyTransform()
        {
            if (canvas == null) return;
            float ws = SuperController.singleton.worldScale;
            float s = 0.0007f * ws * uiScale.val * DpiFactor();
            if (vrMode)
            {
                // VR: the panel is world-space and positioned by BringHud / dragging; only scale here.
                float vs = 0.0007f * uiScale.val;
                canvas.transform.localScale = new Vector3(vs, vs, vs);
                return;
            }
            canvas.transform.localScale = new Vector3(s, s, s);
            canvas.transform.localPosition = new Vector3(posX.val * ws, posY.val * ws, canvasDistance * ws);
            canvas.transform.localEulerAngles = Vector3.zero;
        }

        // ------------------------------------------------------------------
        // VR: placement, pin, attach, depth, persistent layout
        // ------------------------------------------------------------------
        private static bool IsVRMode()
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) return false;
            return sc.isOVR || sc.isOpenVR;
        }

        private Transform ViewTransform()
        {
            try
            {
                if (SuperController.singleton != null && SuperController.singleton.centerCameraTarget != null)
                    return SuperController.singleton.centerCameraTarget.transform;
            }
            catch { }
            Camera c = Camera.main;
            return c != null ? c.transform : null;
        }

        // Put the panel in front of the user at the HUD Pos X/Y offset.
        public void BringHud()
        {
            if (!vrMode || canvas == null) return;
            Transform view = ViewTransform();
            if (view == null) return;
            canvas.transform.SetParent(null, true);
            attachTo.valNoCallback = "None";
            canvas.transform.position = view.position + view.forward * canvasDistance
                + view.right * posX.val + view.up * posY.val;
            canvas.transform.rotation = view.rotation;
            if (pinToView.val) CapturePinOffset();
        }

        private void CapturePinOffset()
        {
            Transform view = ViewTransform();
            if (view == null || canvas == null) return;
            pinnedLocalPos = view.InverseTransformPoint(canvas.transform.position);
            pinnedLocalRot = Quaternion.Inverse(view.rotation) * canvas.transform.rotation;
            pinCaptured = true;
        }

        public void OnVRDragged()
        {
            if (pinToView != null && pinToView.val) CapturePinOffset();
        }

        private void OnPinChanged(bool on)
        {
            if (!vrMode) { RefreshPinButton(); return; }
            if (on)
            {
                CapturePinOffset();
                if (attachTo != null && attachTo.val != "None") attachTo.val = "None";
            }
            else pinCaptured = false;
            RefreshPinButton();
        }

        private void RefreshPinButton()
        {
            bool on = pinToView != null && pinToView.val;
            if (pinBtnImg != null) pinBtnImg.color = on ? new Color(0.36f, 0.58f, 0.90f, 1f) : new Color(0.20f, 0.22f, 0.28f, 1f);
            if (pinBtnTxt != null) pinBtnTxt.text = on ? "PINNED" : "PIN";
        }

        private static Transform HandTransform(bool right)
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) return null;
            if (sc.isOVR) return right ? sc.touchObjectRight : sc.touchObjectLeft;
            if (sc.isOpenVR) return right ? sc.viveObjectRight : sc.viveObjectLeft;
            return null;
        }

        private void OnAttachChanged(string val)
        {
            if (!vrMode || canvas == null) return;
            Transform target = null;
            if (val == "Left Hand") target = HandTransform(false);
            else if (val == "Right Hand") target = HandTransform(true);
            if (target == null && val != "None")
                SuperController.LogMessage("ToggleHUD: no matching VR controller found; HUD stays world-space.");
            if (target != null && pinToView.val) pinToView.val = false;
            canvas.transform.SetParent(target, true);
        }

        private void ApplyDepth(bool onTop)
        {
            if (!vrMode || canvas == null) return;
            if (normalDepthMaterial == null)
            {
                normalDepthMaterial = new Material(Shader.Find("UI/Default"));
                normalDepthMaterial.SetInt("unity_GUIZTestMode", 4);
            }
            Material mat = onTop ? null : normalDepthMaterial;
            Graphic[] graphics = canvas.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
                if (graphics[i] != null) graphics[i].material = mat;
        }

        private void LateUpdate()
        {
            if (!vrMode || canvas == null || pinToView == null || !pinToView.val || !pinCaptured) return;
            Transform view = ViewTransform();
            if (view == null) return;
            canvas.transform.position = view.TransformPoint(pinnedLocalPos);
            canvas.transform.rotation = view.rotation * pinnedLocalRot;
        }

        private bool VRFree()
        {
            return vrMode && canvas != null && !pinToView.val && attachTo.val == "None";
        }

        private string VRSigNow()
        {
            if (canvas == null) return "";
            Vector3 p = canvas.transform.position;
            return p.x.ToString("0.###") + "|" + p.y.ToString("0.###") + "|" + p.z.ToString("0.###") + "|" + uiScale.val.ToString("0.###");
        }

        private void SnapshotVR()
        {
            Transform view = ViewTransform();
            if (view == null || canvas == null) return;
            vrLocalPos = view.InverseTransformPoint(canvas.transform.position);
            vrLocalRot = Quaternion.Inverse(view.rotation) * canvas.transform.rotation;
            vrScale = uiScale.val;
            haveVRLayout = true;
        }

        private void WriteVRLayout()
        {
            if (!haveVRLayout) return;
            try
            {
                JSONClass j = new JSONClass();
                j["px"] = new JSONData(vrLocalPos.x); j["py"] = new JSONData(vrLocalPos.y); j["pz"] = new JSONData(vrLocalPos.z);
                j["rx"] = new JSONData(vrLocalRot.x); j["ry"] = new JSONData(vrLocalRot.y);
                j["rz"] = new JSONData(vrLocalRot.z); j["rw"] = new JSONData(vrLocalRot.w);
                j["scale"] = new JSONData(vrScale);
                if (!FileManagerSecure.DirectoryExists(DEF_DIR))
                    FileManagerSecure.CreateDirectory(DEF_DIR);
                FileManagerSecure.WriteAllText(VR_PATH, j.ToString());
            }
            catch (Exception e) { SuperController.LogError("ToggleHUD VR save: " + e.Message); }
        }

        private void LoadVRLayout(bool enforce)
        {
            try
            {
                if (!vrMode || !FileManagerSecure.FileExists(VR_PATH)) return;
                JSONNode n = JSON.Parse(FileManagerSecure.ReadAllText(VR_PATH));
                JSONClass j = n != null ? n.AsObject : null;
                if (j == null || j["px"] == null) return;
                vrLocalPos = new Vector3(j["px"].AsFloat, j["py"].AsFloat, j["pz"].AsFloat);
                Quaternion q = new Quaternion(j["rx"].AsFloat, j["ry"].AsFloat, j["rz"].AsFloat, j["rw"].AsFloat);
                vrLocalRot = (q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f) ? Quaternion.identity : q;
                vrScale = Mathf.Clamp(j["scale"] != null ? j["scale"].AsFloat : uiScale.val, SCALE_MIN, SCALE_MAX);
                haveVRLayout = true;
                ApplyVRPlacement();
                // Scene-stored plugin values restore after Init, so hold the layout briefly.
                if (enforce) vrEnforceUntil = Time.unscaledTime + 1.2f;
            }
            catch (Exception e) { SuperController.LogError("ToggleHUD VR load: " + e.Message); }
        }

        private void ApplyVRPlacement()
        {
            if (!haveVRLayout || !VRFree()) return;
            Transform view = ViewTransform();
            if (view == null) return;
            if (Mathf.Abs(uiScale.val - vrScale) > 0.001f) uiScale.valNoCallback = vrScale;
            ApplyTransform();
            if (scaleBar != null) scaleBar.SetFromValue(uiScale.val);
            UpdateScaleReadout();
            canvas.transform.position = view.TransformPoint(vrLocalPos);
            canvas.transform.rotation = view.rotation * vrLocalRot;
        }

        private void PersistVRTick()
        {
            if (!vrMode || rememberVR == null || !rememberVR.val || !VRFree()) { vrSaveDueAt = -1f; return; }
            if (haveVRLayout && vrEnforceUntil > 0f && Time.unscaledTime < vrEnforceUntil)
            {
                ApplyVRPlacement();
                vrSig = VRSigNow();
                return;
            }
            string sig = VRSigNow();
            if (sig != vrSig) { vrSig = sig; vrSaveDueAt = Time.unscaledTime + 0.8f; }
            else if (vrSaveDueAt > 0f && Time.unscaledTime >= vrSaveDueAt)
            {
                vrSaveDueAt = -1f;
                SnapshotVR();
                WriteVRLayout();
            }
        }

        public void DragBy(Vector2 pixelDelta)
        {
            if (Camera.main == null) return;
            float fovRad = Camera.main.fieldOfView * Mathf.Deg2Rad;
            float worldPerPixel = (2f * canvasDistance * Mathf.Tan(fovRad * 0.5f)) / Mathf.Max(1f, Screen.height);
            posX.val = Mathf.Clamp(posX.val + pixelDelta.x * worldPerPixel, posX.min, posX.max);
            posY.val = Mathf.Clamp(posY.val + pixelDelta.y * worldPerPixel, posY.min, posY.max);
            ApplyTransform();
        }

        // ------------------------------------------------------------------
        // Default settings persistence
        // ------------------------------------------------------------------
        private void SaveDefaults()
        {
            try
            {
                JSONClass j = new JSONClass();
                j["posX"] = new JSONData(posX.val);
                j["posY"] = new JSONData(posY.val);
                j["uiScale"] = new JSONData(uiScale.val);
                j["dpiAware"] = new JSONData(dpiAware.val);
                j["showHud"] = new JSONData(showHud.val);
                j["soft"] = new JSONData(softState.val);
                j["gt"] = new JSONData(gtState.val);
                j["ac"] = new JSONData(acState.val);
                j["hp"] = new JSONData(hpState.val);
                if (!FileManagerSecure.DirectoryExists(DEF_DIR))
                    FileManagerSecure.CreateDirectory(DEF_DIR);
                FileManagerSecure.WriteAllText(DEF_PATH, j.ToString());
                SuperController.LogMessage("ToggleHUD: saved defaults to " + DEF_PATH);
            }
            catch (Exception e) { SuperController.LogError("ToggleHUD save: " + e.Message); }
        }

        private void LoadDefaults()
        {
            try
            {
                if (!FileManagerSecure.FileExists(DEF_PATH)) return;
                string txt = FileManagerSecure.ReadAllText(DEF_PATH);
                if (string.IsNullOrEmpty(txt)) return;
                JSONNode n = JSON.Parse(txt);
                if (n == null) return;
                JSONClass j = n.AsObject;
                if (j == null) return;
                if (j["posX"] != null)     posX.val = j["posX"].AsFloat;
                if (j["posY"] != null)     posY.val = j["posY"].AsFloat;
                if (j["uiScale"] != null)  uiScale.val = Mathf.Clamp(j["uiScale"].AsFloat, SCALE_MIN, SCALE_MAX);
                if (j["dpiAware"] != null) dpiAware.val = j["dpiAware"].AsBool;
                if (j["showHud"] != null)  showHud.val = j["showHud"].AsBool;
                if (j["soft"] != null)     softState.val = j["soft"].AsBool;
                if (j["gt"] != null)       gtState.val = j["gt"].AsBool;
                if (j["ac"] != null)       acState.val = j["ac"].AsBool;
                if (j["hp"] != null)       { hpState.val = j["hp"].AsBool; ApplyHigh(); }
            }
            catch (Exception e) { SuperController.LogError("ToggleHUD load: " + e.Message); }
        }

        // ------------------------------------------------------------------
        // UI helpers
        // ------------------------------------------------------------------
        private GameObject NewUI(string name, Transform parent, float w, float h)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(w, h);
            rt.localScale = Vector3.one;
            rt.anchoredPosition = Vector2.zero;
            return go;
        }

        private void SetAnchoredTop(GameObject go, float y)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
        }

        private Text AddText(GameObject parent, string content, int size, TextAnchor anchor)
        {
            GameObject go = NewUI("Text", parent.transform, 10f, 10f);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Text t = go.AddComponent<Text>();
            t.font = uiFont;
            t.fontSize = size;
            t.alignment = anchor;
            t.text = content;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private Font GetFont()
        {
            Font f = null;
            try { f = (Font)Resources.GetBuiltinResource(typeof(Font), "Arial.ttf"); } catch { }
            if (f == null)
            {
                Font[] all = Resources.FindObjectsOfTypeAll<Font>();
                if (all != null && all.Length > 0) f = all[0];
            }
            return f;
        }

        // ------------------------------------------------------------------
        // Gazed male atom -> penis length morph
        // ------------------------------------------------------------------
        private void Update()
        {
            PersistVRTick();
            // the button mirrors the real preference, so changing it in VaM's own settings shows up here too
            if (hpState != null && Time.unscaledTime - lastHpPoll > 0.5f)
            {
                lastHpPoll = Time.unscaledTime;
                bool actualHp = ReadHigh();
                if (actualHp != hpState.val) hpState.val = actualHp;
            }
            if (Time.unscaledTime - lastGazeScan < 0.25f) return;
            lastGazeScan = Time.unscaledTime;
            try
            {
                Atom a = FindGazedMaleAtom();
                if (a != null && a != gazedMale)
                {
                    BindPenisMorph(a);
                }
                else if (penisMorph != null)
                {
                    // reflect external changes to the morph (e.g. from the appearance UI)
                    if (lengthBar != null) lengthBar.SetFromValue(penisMorph.morphValue);
                    if (lengthReadout != null) lengthReadout.text = penisMorph.morphValue.ToString("0.00");
                }
            }
            catch { }
        }

        private Atom FindGazedMaleAtom()
        {
            Camera cam = Camera.main;
            if (cam == null) return null;
            RaycastHit hit;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out hit, 100f))
                return null;
            if (hit.collider == null) return null;
            Atom a = hit.collider.GetComponentInParent<Atom>();
            if (a == null || a.type != "Person") return null;
            DAZCharacterSelector dcs = a.GetStorableByID("geometry") as DAZCharacterSelector;
            if (dcs == null || dcs.gender != DAZCharacterSelector.Gender.Male) return null;
            return a;
        }

        private void BindPenisMorph(Atom a)
        {
            gazedMale = a;
            penisMorph = null;
            DAZCharacterSelector dcs = a.GetStorableByID("geometry") as DAZCharacterSelector;
            if (dcs == null || dcs.morphsControlUI == null) return;
            penisMorph = FindPenisLengthMorph(dcs.morphsControlUI);
            if (penisMorph == null)
            {
                if (lengthTitle != null)   lengthTitle.text = "  PENIS (no length morph)";
                if (lengthReadout != null) lengthReadout.text = "--";
                return;
            }
            // Widen the morph's own limits so the full 0..10 slider range applies
            // instead of clamping at the morph's default max.
            try { if (penisMorph.min > 0f) penisMorph.min = 0f; } catch { }
            try { if (penisMorph.max < 10f) penisMorph.max = 10f; } catch { }
            if (lengthBar != null)
                lengthBar.SetFromValue(Mathf.Clamp(penisMorph.morphValue, lengthBar.minV, lengthBar.maxV));
            if (lengthTitle != null)
            {
                string nm = a.uid;
                if (nm != null && nm.Length > 14) nm = nm.Substring(0, 14);
                lengthTitle.text = "  PENIS: " + nm;
            }
            if (lengthReadout != null) lengthReadout.text = penisMorph.morphValue.ToString("0.00");
        }

        private DAZMorph FindPenisLengthMorph(GenerateDAZMorphsControlUI ui)
        {
            DAZMorph m = null;
            try { m = ui.GetMorphByDisplayName("Penis Length"); } catch { }
            if (m != null) return m;
            try
            {
                foreach (DAZMorph dm in ui.GetMorphs())
                {
                    if (dm == null || dm.displayName == null) continue;
                    string dn = dm.displayName.ToLower();
                    if (dn.Contains("penis") && dn.Contains("length")) return dm;
                }
            }
            catch { }
            return null;
        }

        // ------------------------------------------------------------------
        public void OnDestroy()
        {
            try
            {
                if (canvas != null)
                {
                    SuperController.singleton.RemoveCanvas(canvas);
                    if (canvas.gameObject != null) GameObject.Destroy(canvas.gameObject);
                }
            }
            catch { }
            if (normalDepthMaterial != null) GameObject.Destroy(normalDepthMaterial);
        }

        // ------------------------------------------------------------------
        // Drag handler - moves the whole HUD in screen space
        // ------------------------------------------------------------------
        private class DragMove : MonoBehaviour, IDragHandler, IPointerDownHandler
        {
            public ToggleHUD owner;
            private Plane dragPlane;
            private Vector3 grabOffset;
            private bool dragValid;

            public void OnPointerDown(PointerEventData e)
            {
                dragValid = false;
                if (owner == null || !owner.vrMode || owner.canvas == null) return;
                Camera cam = e.pressEventCamera != null ? e.pressEventCamera : Camera.main;
                if (cam == null) return;
                Transform win = owner.canvas.transform;
                Ray ray = cam.ScreenPointToRay(e.position);
                dragPlane = new Plane(-cam.transform.forward, win.position);
                float d;
                if (!dragPlane.Raycast(ray, out d)) return;
                grabOffset = win.position - ray.GetPoint(d);
                dragValid = true;
            }

            public void OnDrag(PointerEventData e)
            {
                if (owner == null) return;
                if (!owner.vrMode) { owner.DragBy(e.delta); return; }
                if (!dragValid || owner.canvas == null) return;
                Camera cam = e.pressEventCamera != null ? e.pressEventCamera : Camera.main;
                if (cam == null) return;
                Ray ray = cam.ScreenPointToRay(e.position);
                float d;
                if (!dragPlane.Raycast(ray, out d)) return;
                Transform win = owner.canvas.transform;
                win.position = ray.GetPoint(d) + grabOffset;
                win.rotation = Quaternion.LookRotation(ray.direction, Vector3.up);
                owner.OnVRDragged();
            }
        }

        // ------------------------------------------------------------------
        // Custom scale bar - click/drag on the track to set value
        // ------------------------------------------------------------------
        private class ScaleBar : MonoBehaviour, IDragHandler, IPointerDownHandler
        {
            public ToggleHUD owner;
            public RectTransform track;
            public RectTransform fill;
            public RectTransform handle;
            public float trackWidth;
            public float minV;
            public float maxV;
            public float value;
            public Action<float> onChange;   // routes to OnScaleBar or OnLengthBar

            public void OnPointerDown(PointerEventData e) { ApplyPointer(e); }
            public void OnDrag(PointerEventData e) { ApplyPointer(e); }

            private void ApplyPointer(PointerEventData e)
            {
                if (track == null) return;
                Vector2 lp;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(track, e.position, e.pressEventCamera, out lp))
                    return;
                float half = trackWidth * 0.5f;
                float frac = Mathf.Clamp01((lp.x + half) / trackWidth);
                value = minV + frac * (maxV - minV);
                Reflect(frac);
                if (onChange != null) onChange(value);
                else if (owner != null) owner.OnScaleBar(value);
            }

            public void SetFromValue(float v)
            {
                value = Mathf.Clamp(v, minV, maxV);
                float frac = (maxV > minV) ? (value - minV) / (maxV - minV) : 0f;
                Reflect(frac);
            }

            private void Reflect(float frac)
            {
                if (fill != null)
                {
                    Vector2 sd = fill.sizeDelta;
                    fill.sizeDelta = new Vector2(trackWidth * frac, sd.y);
                }
                if (handle != null)
                {
                    float half = trackWidth * 0.5f;
                    handle.anchoredPosition = new Vector2(-half + trackWidth * frac, 0f);
                }
            }
        }
    }
}
