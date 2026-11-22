        // ==================== VR / DESKTOP NODE HUD (ZeroT) ====================
        // A small panel with one switch per Rigify node group, so the node visuals can be turned on and off without opening the plugin UI.
        // Modelled on zerot.ToggleHUD: in VR it is a free-floating world-space panel you drag with the laser (header or body),
        // with BRING and PIN buttons, Attach To Left/Right Hand, Always On Top and a remembered layout; on the desktop it is parented to the camera.
        //   NODES        master switch for every node visual (same as Shift+E / Shift+R)
        //   LOWER BODY   hip + torso nodes            UPPER BODY   chest + head nodes
        //   L ARM / R ARM / L LEG / R LEG   click cycles OFF -> FK -> IK (IK hides the FK nodes and shows the pole node)
        //   SYMMETRY     real-time symmetry
        // Runtime-compiled script rules: no enums, no $-strings, no ?. operator.

        private const string HUD_DIR = "Saves/PluginData/Skynet/RigifyVRPlus";
        private const string HUD_VR_PATH = "Saves/PluginData/Skynet/RigifyVRPlus/hud_vr_layout.json";
        private const int HUD_ROWS = 8;
        private const float HUD_W = 360f;
        private const float HUD_SCALE_MIN = 0.4f;
        private const float HUD_SCALE_MAX = 2.5f;

        private static int _hudInstances = 0;
        private int _hudIndex;

        private JSONStorableBool _hudShowJSON;
        private JSONStorableBool _hudDesktopJSON;
        private JSONStorableBool _hudPinJSON;
        private JSONStorableBool _hudTopJSON;
        private JSONStorableBool _hudRememberJSON;
        private JSONStorableFloat _hudScaleJSON;
        private JSONStorableFloat _hudPosXJSON;
        private JSONStorableFloat _hudPosYJSON;
        private JSONStorableStringChooser _hudAttachJSON;

        private Canvas _hudCanvas;
        private GameObject _hudPanel;
        private Font _hudFont;
        private bool _hudVR;
        private float _hudDistance = 1.0f;
        private Image _hudPinImg;
        private Text _hudPinTxt;
        private Material _hudDepthMat;
        private Image[] _hudRowBg = new Image[HUD_ROWS];
        private Image[] _hudRowDot = new Image[HUD_ROWS];
        private Text[] _hudRowState = new Text[HUD_ROWS];
        private Vector3 _hudPinPos;
        private Quaternion _hudPinRot = Quaternion.identity;
        private bool _hudPinCaptured;
        private Vector3 _hudVrPos;
        private Quaternion _hudVrRot = Quaternion.identity;
        private float _hudVrScale;
        private bool _hudHaveLayout;
        private float _hudEnforceUntil;
        private float _hudSaveDue = -1f;
        private string _hudSig = "";
        private float _hudNextPoll;
        private int _hudLastStateKey = -1;

        private static readonly string[] HUD_LABELS = { "NODES", "LOWER BODY", "UPPER BODY", "LEFT ARM", "RIGHT ARM", "LEFT LEG", "RIGHT LEG", "SYMMETRY" };
        private static readonly Color HUD_ON = new Color(0.16f, 0.62f, 0.30f, 1f);
        private static readonly Color HUD_OFF = new Color(0.55f, 0.17f, 0.17f, 1f);
        private static readonly Color HUD_IK = new Color(0.72f, 0.58f, 0.10f, 1f);
        private static readonly Color HUD_ON_DOT = new Color(0.45f, 1f, 0.55f, 1f);
        private static readonly Color HUD_OFF_DOT = new Color(1f, 0.45f, 0.45f, 1f);
        private static readonly Color HUD_IK_DOT = new Color(1f, 0.88f, 0.35f, 1f);
        private static readonly Color HUD_HDR = new Color(0.13f, 0.14f, 0.17f, 0.96f);
        private static readonly Color HUD_BG = new Color(0.10f, 0.11f, 0.13f, 0.90f);
        private static readonly Color HUD_BTN = new Color(0.20f, 0.22f, 0.28f, 1f);

        private void HudInitStorables()
        {
            _hudShowJSON = new JSONStorableBool("HUD Show", true);
            _hudDesktopJSON = new JSONStorableBool("HUD Show On Desktop", false);
            _hudPinJSON = new JSONStorableBool("HUD Pin To View (VR)", false);
            _hudTopJSON = new JSONStorableBool("HUD Always On Top (VR)", true);
            _hudRememberJSON = new JSONStorableBool("HUD Remember VR Layout", true);
            _hudScaleJSON = new JSONStorableFloat("HUD Scale", 1.0f, HUD_SCALE_MIN, HUD_SCALE_MAX, true, true);
            _hudPosXJSON = new JSONStorableFloat("HUD Pos X", -0.30f, -1.2f, 1.2f, false, true);
            _hudPosYJSON = new JSONStorableFloat("HUD Pos Y", 0.18f, -1.0f, 1.0f, false, true);
            _hudAttachJSON = new JSONStorableStringChooser("HUD Attach To (VR)", new List<string> { "None", "Left Hand", "Right Hand" }, "None", "HUD Attach To (VR)");

            _hudShowJSON.setCallbackFunction = delegate(bool v) { HudApplyVisible(); };
            _hudDesktopJSON.setCallbackFunction = delegate(bool v) { HudApplyVisible(); };
            _hudScaleJSON.setCallbackFunction = delegate(float v) { HudApplyTransform(); };
            _hudPinJSON.setCallbackFunction = delegate(bool v) { HudOnPinChanged(v); };
            _hudTopJSON.setCallbackFunction = delegate(bool v) { HudApplyDepth(v); };
            _hudRememberJSON.setCallbackFunction = delegate(bool v) { if (v) HudLoadVRLayout(true); };
            _hudAttachJSON.setCallbackFunction = delegate(string v) { HudOnAttachChanged(v); };

            RegisterBool(_hudShowJSON);
            RegisterBool(_hudDesktopJSON);
            RegisterBool(_hudPinJSON);
            RegisterBool(_hudTopJSON);
            RegisterBool(_hudRememberJSON);
            RegisterFloat(_hudScaleJSON);
            RegisterFloat(_hudPosXJSON);
            RegisterFloat(_hudPosYJSON);
            RegisterStringChooser(_hudAttachJSON);

            RegisterAction(new JSONStorableAction("HUD Show", () => { _hudShowJSON.val = true; }));
            RegisterAction(new JSONStorableAction("HUD Hide", () => { _hudShowJSON.val = false; }));
            RegisterAction(new JSONStorableAction("HUD Bring", () => { HudBring(); }));
            RegisterAction(new JSONStorableAction("All Nodes On", () => { HudAllNodes(true); }));
            RegisterAction(new JSONStorableAction("All Nodes Off", () => { HudAllNodes(false); }));
        }

        private void HudAddPluginControls()
        {
            _mainSettingsControls.Add(CreateSpacerControl(false, 20f));
            _mainSettingsControls.Add(CreateDisplayText("<b>--- Node HUD ---</b>", false, 34, 50f));
            _mainSettingsControls.Add(CreateToggle(_hudShowJSON, false));
            _mainSettingsControls.Add(CreateToggle(_hudDesktopJSON, false));
            _mainSettingsControls.Add(CreateSlider(_hudScaleJSON, false));
            _mainSettingsControls.Add(CreateToggle(_hudPinJSON, false));
            _mainSettingsControls.Add(CreateToggle(_hudTopJSON, false));
            _mainSettingsControls.Add(CreateToggle(_hudRememberJSON, false));
            _mainSettingsControls.Add(CreatePopup(_hudAttachJSON, false));
            UIDynamicButton bringBtn = CreateButton("Bring HUD To Me", false);
            bringBtn.button.onClick.AddListener(() => HudBring());
            _mainSettingsControls.Add(bringBtn);
            UIDynamicButton allOnBtn = CreateButton("All Nodes On", false);
            allOnBtn.button.onClick.AddListener(() => HudAllNodes(true));
            _mainSettingsControls.Add(allOnBtn);
            UIDynamicButton allOffBtn = CreateButton("All Nodes Off", false);
            allOffBtn.button.onClick.AddListener(() => HudAllNodes(false));
            _mainSettingsControls.Add(allOffBtn);
        }

        private static bool HudIsVR()
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) return false;
            return sc.isOVR || sc.isOpenVR;
        }

        private Transform HudView()
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

        // ---- row state: 0 = off, 1 = on / FK, 2 = IK -------------------------------------------------------------
        private int HudRowState(int row)
        {
            switch (row)
            {
                case 0: return (_enabledJSON != null && _enabledJSON.val) ? 1 : 0;
                case 1: return (_torsoNodeEnabledJSON != null && _torsoNodeEnabledJSON.val) ? 1 : 0;
                case 2: return (_upperBodyNodeEnabledJSON != null && _upperBodyNodeEnabledJSON.val) ? 1 : 0;
                case 3: return HudLimbState(_leftArmNodeEnabledJSON, _leftArmIKEnabledJSON);
                case 4: return HudLimbState(_rightArmNodeEnabledJSON, _rightArmIKEnabledJSON);
                case 5: return HudLimbState(_leftLegNodeEnabledJSON, _leftLegIKEnabledJSON);
                case 6: return HudLimbState(_rightLegNodeEnabledJSON, _rightLegIKEnabledJSON);
                case 7: return (_realtimeSymmetryJSON != null && _realtimeSymmetryJSON.val) ? 1 : 0;
            }
            return 0;
        }

        private static int HudLimbState(JSONStorableBool node, JSONStorableBool ik)
        {
            if (ik != null && ik.val) return 2;
            if (node != null && node.val) return 1;
            return 0;
        }

        private static void HudSetLimb(JSONStorableBool node, JSONStorableBool ik, int state)
        {
            // same meaning as the screen buttons: click = off, Ctrl+click = FK, Shift+click = IK
            if (state == 2) { if (ik != null) ik.val = true; if (node != null) node.val = true; }
            else if (state == 1) { if (node != null) node.val = true; if (ik != null) ik.val = false; }
            else { if (node != null) node.val = false; if (ik != null) ik.val = false; }
        }

        private void HudRowClick(int row)
        {
            int s = HudRowState(row);
            switch (row)
            {
                case 0: _enabledJSON.val = !_enabledJSON.val; break;
                case 1: _torsoNodeEnabledJSON.val = !_torsoNodeEnabledJSON.val; break;
                case 2: _upperBodyNodeEnabledJSON.val = !_upperBodyNodeEnabledJSON.val; break;
                case 3: HudSetLimb(_leftArmNodeEnabledJSON, _leftArmIKEnabledJSON, (s + 1) % 3); break;
                case 4: HudSetLimb(_rightArmNodeEnabledJSON, _rightArmIKEnabledJSON, (s + 1) % 3); break;
                case 5: HudSetLimb(_leftLegNodeEnabledJSON, _leftLegIKEnabledJSON, (s + 1) % 3); break;
                case 6: HudSetLimb(_rightLegNodeEnabledJSON, _rightLegIKEnabledJSON, (s + 1) % 3); break;
                case 7: _realtimeSymmetryJSON.val = !_realtimeSymmetryJSON.val; break;
            }
            HudRepaint();
        }

        // every node group at once: on = node visuals on, FK; off = node groups hidden (the master switch is left alone)
        private void HudAllNodes(bool on)
        {
            if (on) _enabledJSON.val = true;
            if (_torsoNodeEnabledJSON != null) _torsoNodeEnabledJSON.val = on;
            if (_upperBodyNodeEnabledJSON != null) _upperBodyNodeEnabledJSON.val = on;
            HudSetLimb(_leftArmNodeEnabledJSON, _leftArmIKEnabledJSON, on ? 1 : 0);
            HudSetLimb(_rightArmNodeEnabledJSON, _rightArmIKEnabledJSON, on ? 1 : 0);
            HudSetLimb(_leftLegNodeEnabledJSON, _leftLegIKEnabledJSON, on ? 1 : 0);
            HudSetLimb(_rightLegNodeEnabledJSON, _rightLegIKEnabledJSON, on ? 1 : 0);
            HudRepaint();
        }

        private void HudRepaint()
        {
            int key = 0;
            for (int r = 0; r < HUD_ROWS; r++)
            {
                int s = HudRowState(r);
                key = key * 3 + s;
                if (_hudRowBg[r] == null) continue;
                bool isLimb = r >= 3 && r <= 6;
                _hudRowBg[r].color = s == 2 ? HUD_IK : (s == 1 ? HUD_ON : HUD_OFF);
                if (_hudRowDot[r] != null) _hudRowDot[r].color = s == 2 ? HUD_IK_DOT : (s == 1 ? HUD_ON_DOT : HUD_OFF_DOT);
                if (_hudRowState[r] != null) _hudRowState[r].text = s == 0 ? "OFF" : (s == 2 ? "IK" : (isLimb ? "FK" : "ON"));
            }
            _hudLastStateKey = key;
        }

        // ---- construction ---------------------------------------------------------------------------------------
        private void HudBuild()
        {
            try
            {
                if (_hudCanvas != null) return;
                _hudFont = HudGetFont();
                _hudIndex = _hudInstances++;
                GameObject canvasGO = new GameObject("RigifyHUD_Canvas");
                canvasGO.transform.SetParent(transform, false);
                _hudCanvas = canvasGO.AddComponent<Canvas>();
                _hudCanvas.renderMode = RenderMode.WorldSpace;
                _hudCanvas.pixelPerfect = false;
                SuperController.singleton.AddCanvas(_hudCanvas);
                CanvasScaler cs = canvasGO.AddComponent<CanvasScaler>();
                cs.dynamicPixelsPerUnit = 1f;
                canvasGO.AddComponent<GraphicRaycaster>();

                _hudVR = HudIsVR();
                if (_hudVR) canvasGO.transform.SetParent(null, false);
                else if (Camera.main != null) canvasGO.transform.SetParent(Camera.main.transform, false);

                float height = 54f + HUD_ROWS * 58f + 62f;
                _hudPanel = HudNew("Panel", canvasGO.transform, HUD_W, height);
                Image bg = _hudPanel.AddComponent<Image>();
                bg.color = HUD_BG;
                RigifyHudDrag dm = _hudPanel.AddComponent<RigifyHudDrag>();
                dm.owner = this;

                GameObject header = HudNew("Header", _hudPanel.transform, HUD_W, 46f);
                HudTop(header, 0f);
                Image hi = header.AddComponent<Image>();
                hi.color = HUD_HDR;
                RigifyHudDrag dmh = header.AddComponent<RigifyHudDrag>();
                dmh.owner = this;
                string who = _personAtom != null ? _personAtom.uid : containingAtom.uid;
                if (who != null && who.Length > 12) who = who.Substring(0, 12);
                Text ht = HudText(header, "  RIGIFY  " + who, 19, TextAnchor.MiddleLeft);
                ht.color = new Color(0.85f, 0.87f, 0.92f, 1f);
                Text hh = HudText(header, "(drag)   ", 15, TextAnchor.MiddleRight);
                hh.color = new Color(0.5f, 0.53f, 0.6f, 1f);
                if (_hudVR)
                {
                    // BRING / PIN sit next to the header (not inside it) so pressing them never starts a drag
                    ht.text = "RIGIFY  " + who;
                    ht.alignment = TextAnchor.MiddleCenter;
                    hh.text = "";
                    Image bi;
                    Text bt;
                    HudHeaderButton(_hudPanel.transform, "BRING", -144f, out bi, out bt, delegate { HudBring(); });
                    HudHeaderButton(_hudPanel.transform, "PIN", 144f, out _hudPinImg, out _hudPinTxt, delegate { _hudPinJSON.val = !_hudPinJSON.val; });
                    HudRefreshPinButton();
                }

                for (int r = 0; r < HUD_ROWS; r++)
                {
                    int row = r;
                    _hudRowBg[r] = HudRow(_hudPanel.transform, -54f - r * 58f, HUD_LABELS[r], out _hudRowState[r], out _hudRowDot[r], delegate { HudRowClick(row); });
                }

                // footer: ALL ON / ALL OFF / SIZE - / SIZE +
                float fy = -54f - HUD_ROWS * 58f - 2f;
                HudFooterButton(_hudPanel.transform, "ALL ON", -126f, fy, 78f, delegate { HudAllNodes(true); });
                HudFooterButton(_hudPanel.transform, "ALL OFF", -42f, fy, 78f, delegate { HudAllNodes(false); });
                HudFooterButton(_hudPanel.transform, "SIZE -", 42f, fy, 78f, delegate { _hudScaleJSON.val = Mathf.Clamp(_hudScaleJSON.val - 0.1f, HUD_SCALE_MIN, HUD_SCALE_MAX); });
                HudFooterButton(_hudPanel.transform, "SIZE +", 126f, fy, 78f, delegate { _hudScaleJSON.val = Mathf.Clamp(_hudScaleJSON.val + 0.1f, HUD_SCALE_MIN, HUD_SCALE_MAX); });

                HudApplyVisible();
                HudApplyTransform();
                if (_hudVR)
                {
                    HudBring();
                    HudApplyDepth(_hudTopJSON.val);
                    if (_hudRememberJSON.val) HudLoadVRLayout(true);
                }
                HudRepaint();
            }
            catch (Exception e)
            {
                SuperController.LogError(PLUGIN_NAME + " HUD: " + e.ToString());
            }
        }

        private void HudHeaderButton(Transform parent, string label, float x, out Image img, out Text txt, UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = HudNew("Btn_" + label, parent, 64f, 34f);
            HudTop(go, -6f);
            go.GetComponent<RectTransform>().anchoredPosition = new Vector2(x, -6f);
            img = go.AddComponent<Image>();
            img.color = HUD_BTN;
            Button b = go.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(onClick);
            txt = HudText(go, label, 14, TextAnchor.MiddleCenter);
            txt.fontStyle = FontStyle.Bold;
        }

        private void HudFooterButton(Transform parent, string label, float x, float y, float w, UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = HudNew("Btn_" + label, parent, w, 44f);
            HudTop(go, y);
            go.GetComponent<RectTransform>().anchoredPosition = new Vector2(x, y);
            Image img = go.AddComponent<Image>();
            img.color = HUD_BTN;
            Button b = go.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(onClick);
            Text t = HudText(go, label, 15, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
        }

        private Image HudRow(Transform parent, float top, string label, out Text stateTxt, out Image dot, UnityEngine.Events.UnityAction onClick)
        {
            GameObject row = HudNew("Row_" + label, parent, 336f, 52f);
            HudTop(row, top);
            Image bimg = row.AddComponent<Image>();
            bimg.color = HUD_OFF;
            Button b = row.AddComponent<Button>();
            b.targetGraphic = bimg;
            b.onClick.AddListener(onClick);

            GameObject dgo = HudNew("Dot", row.transform, 22f, 22f);
            RectTransform drt = dgo.GetComponent<RectTransform>();
            drt.anchorMin = new Vector2(0f, 0.5f);
            drt.anchorMax = new Vector2(0f, 0.5f);
            drt.anchoredPosition = new Vector2(24f, 0f);
            dot = dgo.AddComponent<Image>();
            dot.color = HUD_OFF_DOT;
            dot.raycastTarget = false;

            Text lt = HudText(row, "         " + label, 19, TextAnchor.MiddleLeft);
            lt.color = Color.white;

            GameObject sgo = HudNew("State", row.transform, 90f, 40f);
            RectTransform srt = sgo.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(1f, 0.5f);
            srt.anchorMax = new Vector2(1f, 0.5f);
            srt.anchoredPosition = new Vector2(-52f, 0f);
            stateTxt = sgo.AddComponent<Text>();
            stateTxt.font = _hudFont;
            stateTxt.fontSize = 20;
            stateTxt.fontStyle = FontStyle.Bold;
            stateTxt.alignment = TextAnchor.MiddleRight;
            stateTxt.text = "OFF";
            stateTxt.color = Color.white;
            stateTxt.raycastTarget = false;
            return bimg;
        }

        private GameObject HudNew(string name, Transform parent, float w, float h)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(w, h);
            rt.localScale = Vector3.one;
            rt.anchoredPosition = Vector2.zero;
            return go;
        }

        private void HudTop(GameObject go, float y)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
        }

        private Text HudText(GameObject parent, string content, int size, TextAnchor anchor)
        {
            GameObject go = HudNew("Text", parent.transform, 10f, 10f);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Text t = go.AddComponent<Text>();
            t.font = _hudFont;
            t.fontSize = size;
            t.alignment = anchor;
            t.text = content;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private Font HudGetFont()
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

        // ---- visibility / placement ----------------------------------------------------------------------------
        private void HudApplyVisible()
        {
            if (_hudPanel == null) return;
            bool show = _hudShowJSON.val && (_hudVR || _hudDesktopJSON.val) && isActiveAndEnabled;
            if (_hudPanel.activeSelf != show) _hudPanel.SetActive(show);
        }

        private float HudDpi()
        {
            float d = Screen.dpi;
            if (d < 1f) return 1f;
            return Mathf.Clamp(d / 96f, 0.5f, 3f);
        }

        private void HudApplyTransform()
        {
            if (_hudCanvas == null) return;
            float ws = SuperController.singleton.worldScale;
            if (_hudVR)
            {
                float vs = 0.0007f * _hudScaleJSON.val;
                _hudCanvas.transform.localScale = new Vector3(vs, vs, vs);
                return;
            }
            float s = 0.0007f * ws * _hudScaleJSON.val * HudDpi();
            _hudCanvas.transform.localScale = new Vector3(s, s, s);
            _hudCanvas.transform.localPosition = new Vector3((_hudPosXJSON.val + 0.45f * _hudIndex) * ws, _hudPosYJSON.val * ws, _hudDistance * ws);
            _hudCanvas.transform.localEulerAngles = Vector3.zero;
        }

        // put the panel in front of the user (VR); the instances fan out sideways so two characters do not overlap
        public void HudBring()
        {
            if (!_hudVR || _hudCanvas == null) return;
            Transform view = HudView();
            if (view == null) return;
            _hudCanvas.transform.SetParent(null, true);
            _hudAttachJSON.valNoCallback = "None";
            _hudCanvas.transform.position = view.position + view.forward * _hudDistance + view.right * (_hudPosXJSON.val + 0.45f * _hudIndex) + view.up * _hudPosYJSON.val;
            _hudCanvas.transform.rotation = view.rotation;
            if (_hudPinJSON.val) HudCapturePin();
        }

        private void HudCapturePin()
        {
            Transform view = HudView();
            if (view == null || _hudCanvas == null) return;
            _hudPinPos = view.InverseTransformPoint(_hudCanvas.transform.position);
            _hudPinRot = Quaternion.Inverse(view.rotation) * _hudCanvas.transform.rotation;
            _hudPinCaptured = true;
        }

        public void HudDragged()
        {
            if (_hudPinJSON != null && _hudPinJSON.val) HudCapturePin();
        }

        private void HudOnPinChanged(bool on)
        {
            if (!_hudVR) { HudRefreshPinButton(); return; }
            if (on)
            {
                HudCapturePin();
                if (_hudAttachJSON != null && _hudAttachJSON.val != "None") _hudAttachJSON.val = "None";
            }
            else _hudPinCaptured = false;
            HudRefreshPinButton();
        }

        private void HudRefreshPinButton()
        {
            bool on = _hudPinJSON != null && _hudPinJSON.val;
            if (_hudPinImg != null) _hudPinImg.color = on ? new Color(0.36f, 0.58f, 0.90f, 1f) : HUD_BTN;
            if (_hudPinTxt != null) _hudPinTxt.text = on ? "PINNED" : "PIN";
        }

        private static Transform HudHand(bool right)
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) return null;
            if (sc.isOVR) return right ? sc.touchObjectRight : sc.touchObjectLeft;
            if (sc.isOpenVR) return right ? sc.viveObjectRight : sc.viveObjectLeft;
            return null;
        }

        private void HudOnAttachChanged(string val)
        {
            if (!_hudVR || _hudCanvas == null) return;
            Transform target = null;
            if (val == "Left Hand") target = HudHand(false);
            else if (val == "Right Hand") target = HudHand(true);
            if (target == null && val != "None")
                SuperController.LogMessage(PLUGIN_NAME + ": no matching VR controller found; the HUD stays world-space.");
            if (target != null && _hudPinJSON.val) _hudPinJSON.val = false;
            _hudCanvas.transform.SetParent(target, true);
        }

        // always on top: the default UI material ignores depth; otherwise a depth-tested copy is used
        private void HudApplyDepth(bool onTop)
        {
            if (!_hudVR || _hudCanvas == null) return;
            if (_hudDepthMat == null)
            {
                _hudDepthMat = new Material(Shader.Find("UI/Default"));
                _hudDepthMat.SetInt("unity_GUIZTestMode", 4);
            }
            Material mat = onTop ? null : _hudDepthMat;
            Graphic[] graphics = _hudCanvas.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
                if (graphics[i] != null) graphics[i].material = mat;
        }

        private bool HudVRFree()
        {
            return _hudVR && _hudCanvas != null && !_hudPinJSON.val && _hudAttachJSON.val == "None";
        }

        private string HudSigNow()
        {
            if (_hudCanvas == null) return "";
            Vector3 p = _hudCanvas.transform.position;
            return p.x.ToString("0.###") + "|" + p.y.ToString("0.###") + "|" + p.z.ToString("0.###") + "|" + _hudScaleJSON.val.ToString("0.###");
        }

        private void HudSnapshotVR()
        {
            Transform view = HudView();
            if (view == null || _hudCanvas == null) return;
            _hudVrPos = view.InverseTransformPoint(_hudCanvas.transform.position);
            _hudVrRot = Quaternion.Inverse(view.rotation) * _hudCanvas.transform.rotation;
            _hudVrScale = _hudScaleJSON.val;
            _hudHaveLayout = true;
        }

        private void HudWriteVRLayout()
        {
            if (!_hudHaveLayout) return;
            try
            {
                JSONClass j = new JSONClass();
                j["px"] = new JSONData(_hudVrPos.x); j["py"] = new JSONData(_hudVrPos.y); j["pz"] = new JSONData(_hudVrPos.z);
                j["rx"] = new JSONData(_hudVrRot.x); j["ry"] = new JSONData(_hudVrRot.y);
                j["rz"] = new JSONData(_hudVrRot.z); j["rw"] = new JSONData(_hudVrRot.w);
                j["scale"] = new JSONData(_hudVrScale);
                if (!FileManagerSecure.DirectoryExists(HUD_DIR))
                    FileManagerSecure.CreateDirectory(HUD_DIR);
                FileManagerSecure.WriteAllText(HUD_VR_PATH, j.ToString());
            }
            catch (Exception e) { SuperController.LogError(PLUGIN_NAME + " HUD layout save: " + e.Message); }
        }

        private void HudLoadVRLayout(bool enforce)
        {
            try
            {
                if (!_hudVR || !FileManagerSecure.FileExists(HUD_VR_PATH)) return;
                JSONNode n = JSON.Parse(FileManagerSecure.ReadAllText(HUD_VR_PATH));
                JSONClass j = n != null ? n.AsObject : null;
                if (j == null || j["px"] == null) return;
                _hudVrPos = new Vector3(j["px"].AsFloat, j["py"].AsFloat, j["pz"].AsFloat);
                Quaternion q = new Quaternion(j["rx"].AsFloat, j["ry"].AsFloat, j["rz"].AsFloat, j["rw"].AsFloat);
                _hudVrRot = (q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f) ? Quaternion.identity : q;
                _hudVrScale = Mathf.Clamp(j["scale"] != null ? j["scale"].AsFloat : _hudScaleJSON.val, HUD_SCALE_MIN, HUD_SCALE_MAX);
                _hudHaveLayout = true;
                HudApplyVRPlacement();
                // scene-stored plugin values restore after Init, so hold the layout for a moment
                if (enforce) _hudEnforceUntil = Time.unscaledTime + 1.2f;
            }
            catch (Exception e) { SuperController.LogError(PLUGIN_NAME + " HUD layout load: " + e.Message); }
        }

        private void HudApplyVRPlacement()
        {
            if (!_hudHaveLayout || !HudVRFree()) return;
            Transform view = HudView();
            if (view == null) return;
            if (Mathf.Abs(_hudScaleJSON.val - _hudVrScale) > 0.001f) _hudScaleJSON.valNoCallback = _hudVrScale;
            HudApplyTransform();
            _hudCanvas.transform.position = view.TransformPoint(_hudVrPos);
            _hudCanvas.transform.rotation = view.rotation * _hudVrRot;
        }

        private void HudPersistTick()
        {
            if (!_hudVR || _hudRememberJSON == null || !_hudRememberJSON.val || !HudVRFree()) { _hudSaveDue = -1f; return; }
            if (_hudHaveLayout && _hudEnforceUntil > 0f && Time.unscaledTime < _hudEnforceUntil)
            {
                HudApplyVRPlacement();
                _hudSig = HudSigNow();
                return;
            }
            string sig = HudSigNow();
            if (sig != _hudSig) { _hudSig = sig; _hudSaveDue = Time.unscaledTime + 0.8f; }
            else if (_hudSaveDue > 0f && Time.unscaledTime >= _hudSaveDue)
            {
                _hudSaveDue = -1f;
                HudSnapshotVR();
                HudWriteVRLayout();
            }
        }

        public void HudDragBy(Vector2 pixelDelta)
        {
            if (Camera.main == null) return;
            float fovRad = Camera.main.fieldOfView * Mathf.Deg2Rad;
            float worldPerPixel = (2f * _hudDistance * Mathf.Tan(fovRad * 0.5f)) / Mathf.Max(1f, Screen.height);
            _hudPosXJSON.val = Mathf.Clamp(_hudPosXJSON.val + pixelDelta.x * worldPerPixel, _hudPosXJSON.min, _hudPosXJSON.max);
            _hudPosYJSON.val = Mathf.Clamp(_hudPosYJSON.val + pixelDelta.y * worldPerPixel, _hudPosYJSON.min, _hudPosYJSON.max);
            HudApplyTransform();
        }

        // runs from Update even while the node visuals are off, so the HUD stays usable
        private void HudUpdate()
        {
            if (_hudCanvas == null) return;
            HudApplyVisible();
            HudPersistTick();
            // the HUD mirrors the real settings (Shift+E, the screen buttons and the plugin UI change them too)
            if (Time.unscaledTime >= _hudNextPoll)
            {
                _hudNextPoll = Time.unscaledTime + 0.25f;
                int key = 0;
                for (int r = 0; r < HUD_ROWS; r++) key = key * 3 + HudRowState(r);
                if (key != _hudLastStateKey) HudRepaint();
            }
        }

        private void HudLateUpdate()
        {
            if (!_hudVR || _hudCanvas == null || _hudPinJSON == null || !_hudPinJSON.val || !_hudPinCaptured) return;
            Transform view = HudView();
            if (view == null) return;
            _hudCanvas.transform.position = view.TransformPoint(_hudPinPos);
            _hudCanvas.transform.rotation = view.rotation * _hudPinRot;
        }

        private void HudDestroy()
        {
            try
            {
                if (_hudCanvas != null)
                {
                    SuperController.singleton.RemoveCanvas(_hudCanvas);
                    if (_hudCanvas.gameObject != null) GameObject.Destroy(_hudCanvas.gameObject);
                    _hudCanvas = null;
                    if (_hudInstances > 0) _hudInstances--;
                }
            }
            catch { }
            if (_hudDepthMat != null) GameObject.Destroy(_hudDepthMat);
        }

        // drag handler: VR = move the world-space panel along the laser ray; desktop = move it in screen space
        private class RigifyHudDrag : MonoBehaviour, IDragHandler, IPointerDownHandler
        {
            public Rigify owner;
            private Plane dragPlane;
            private Vector3 grabOffset;
            private bool dragValid;

            public void OnPointerDown(PointerEventData e)
            {
                dragValid = false;
                if (owner == null || !owner._hudVR || owner._hudCanvas == null) return;
                Camera cam = e.pressEventCamera != null ? e.pressEventCamera : Camera.main;
                if (cam == null) return;
                Transform win = owner._hudCanvas.transform;
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
                if (!owner._hudVR) { owner.HudDragBy(e.delta); return; }
                if (!dragValid || owner._hudCanvas == null) return;
                Camera cam = e.pressEventCamera != null ? e.pressEventCamera : Camera.main;
                if (cam == null) return;
                Ray ray = cam.ScreenPointToRay(e.position);
                float d;
                if (!dragPlane.Raycast(ray, out d)) return;
                Transform win = owner._hudCanvas.transform;
                win.position = ray.GetPoint(d) + grabOffset;
                win.rotation = Quaternion.LookRotation(ray.direction, Vector3.up);
                owner.HudDragged();
            }
        }

