using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using HookesLawLab.Physics;
using HookesLawLab.UI;
using HookesLawLab.Narration;

namespace HookesLawLab.Teacher
{
    public enum TeacherConversationState
    {
        Idle,
        NearTeacher,
        ConversationOpen,
        TeacherSpeaking,
        ConversationClosed
    }

    /// <summary>
    /// Interactive AI Physics Teacher (Prof. Anam) for VR and Desktop.
    /// Handles proximity activation, 6-state dialogue UI, bilingual EN/HI pre-recorded audio lessons,
    /// custom question matching, and procedural breathing, gaze, and speaking cadence gestures.
    /// </summary>
    public class PhysicsTeacher : MonoBehaviour
    {
        [Serializable]
        public class QuestionEntry
        {
            public string id;
            public string textEn;
            public string textHi;
            public string tagEn;
            public string tagHi;
            public Func<string, string> getAnswer;
        }

        public float activationDistance = 2.2f;
        public float deactivationDistance = 2.7f;
        public TeacherConversationState State { get; private set; } = TeacherConversationState.Idle;

        private SpringSimulation _springSim;
        private Transform _teacherRig;
        private Vector3 _teacherBasePos;
        private AudioSource _voiceSource;
        private GuidedExperimentManager _guideCoordinator;

        public AudioSource VoiceSource => _voiceSource;
        public bool IsTeacherBusy =>
            _isSpeaking ||
            State == TeacherConversationState.ConversationOpen ||
            State == TeacherConversationState.TeacherSpeaking;
        public bool IsAudioServiceReady =>
            _voiceSource != null &&
            _voiceSource.enabled &&
            _voiceSource.gameObject.activeInHierarchy;

        public event Action OnTeacherHelpOpened;
        public event Action OnTeacherLessonStarted;
        public event Action OnTeacherLessonEnded;

        public void SetExternalVoiceCoordinator(GuidedExperimentManager guide)
        {
            _guideCoordinator = guide;
        }

        public void SetVoiceVolume(float volume)
        {
            if (_voiceSource != null) _voiceSource.volume = Mathf.Clamp01(volume);
        }

        /// <summary>
        /// Reuses PhysicsTeacher's verified Quest/Editor AudioSource for fixed 2D guided instructions
        /// when the teacher is not engaged in a user-initiated lesson/conversation.
        /// </summary>
        public bool PlayGuidedClip(AudioClip clip, float volume)
        {
            if (IsTeacherBusy)
            {
                Debug.Log("[PhysicsTeacher] Denied guided clip playback because teacher conversation is active.");
                return false;
            }
            if (!IsAudioServiceReady || clip == null || clip.length <= 0.01f || clip.loadState == AudioDataLoadState.Failed)
            {
                return false;
            }

            _voiceSource.Stop();
            _voiceSource.clip = clip;
            _voiceSource.loop = false;
            _voiceSource.mute = false;
            _voiceSource.priority = 0;
            _voiceSource.spatialBlend = 0f; // 2D non-attenuated playback for guided instructions
            _voiceSource.outputAudioMixerGroup = null;
            _voiceSource.bypassEffects = true;
            _voiceSource.bypassListenerEffects = true;
            _voiceSource.bypassReverbZones = true;
            _voiceSource.volume = Mathf.Clamp01(volume);
            _voiceSource.Play();
            return _voiceSource.isPlaying;
        }

        public void PauseGuidedClip()
        {
            if (!_isSpeaking && _voiceSource != null && _voiceSource.isPlaying)
            {
                _voiceSource.Pause();
            }
        }

        public void UnPauseGuidedClip()
        {
            if (!IsTeacherBusy && _voiceSource != null && _voiceSource.clip != null && !_voiceSource.isPlaying)
            {
                _voiceSource.UnPause();
            }
        }

        public void StopGuidedClip()
        {
            if (!_isSpeaking && _voiceSource != null && _voiceSource.isPlaying)
            {
                _voiceSource.Stop();
            }
        }

        public bool IsGuidedClipPlaying()
        {
            return !_isSpeaking && _voiceSource != null && _voiceSource.isPlaying;
        }

        private string _activeLanguage = "en";
        private QuestionEntry _activeQuestion;
        private string _activeAnswer = "";
        private float _animTime;
        private bool _isSpeaking;
        private float _explanatoryLean;

        private readonly List<QuestionEntry> _questions = new List<QuestionEntry>();

        // 3D World-Space Dialogue UI
        private GameObject _dialogRoot;
        private Vector3 _dialogBasePos;
        private GameObject _proximityView;
        private GameObject _menuView;
        private GameObject _answerView;
        private TMP_Text _telemetrySubText;
        private TMP_Text _langBtnLabel;
        private readonly List<TMP_Text> _questionLabels = new List<TMP_Text>();
        private TMP_Text _answerQuestionText;
        private TMP_Text _answerBodyText;
        private TMP_Text _speakingBadgeText;
        private readonly Image[] _waveformBars = new Image[12];
        private TMP_InputField _customInputField;

        public void Initialize(
            Transform teacherRig,
            Vector3 dialogWorldPos,
            SpringSimulation sim,
            TMP_FontAsset fontAsset = null)
        {
            _teacherRig = teacherRig;
            _teacherBasePos = teacherRig != null ? teacherRig.position : new Vector3(3.3186f, 0f, 0.005f);
            _springSim = sim;

            _voiceSource = gameObject.AddComponent<AudioSource>();
            _voiceSource.playOnAwake = false;
            _voiceSource.spatialBlend = 0.65f;
            _voiceSource.minDistance = 1.5f;
            _voiceSource.maxDistance = 8.0f;

            InitKnowledgeBase();
            BuildDialogUI(dialogWorldPos, fontAsset);
            SetState(TeacherConversationState.Idle);
        }

        private void InitKnowledgeBase()
        {
            _questions.Clear();
            _questions.Add(new QuestionEntry
            {
                id = "q1",
                textEn = "What is Hooke's Law?",
                textHi = "हुक का नियम क्या है?",
                tagEn = "Core Physics Law",
                tagHi = "भौतिकी का मूल सिद्धांत",
                getAnswer = lang => lang == "hi"
                    ? "हुक का नियम भौतिकी का एक मूलभूत सिद्धांत है जिसके अनुसार किसी स्प्रिंग को खींचने या दबाने में लगने वाला बल उसके विस्थापन के सीधे समानुपाती होता है, बशर्ते स्प्रिंग अपनी प्रत्यास्थता सीमा के भीतर रहे। गणितीय रूप से इसे F = -kx लिखा जाता है।"
                    : "Hooke's Law is a fundamental principle of physics stating that the force needed to extend or compress a spring by some distance is directly proportional to that distance, provided the spring remains within its elastic limit. Mathematically, it is written as F equals minus k x."
            });
            _questions.Add(new QuestionEntry
            {
                id = "q2",
                textEn = "Why do we use Hooke's Law?",
                textHi = "हम हुक के नियम का उपयोग क्यों करते हैं?",
                tagEn = "Practical Applications",
                tagHi = "व्यावहारिक अनुप्रयोग",
                getAnswer = lang => lang == "hi"
                    ? "हम इंजीनियरिंग में लोचदार प्रणालियों जैसे गाड़ियों के सस्पेंशन, सटीक वेइंग स्केल, शॉक एब्जॉर्बर और भूकंप-रोधी संरचनाओं को डिजाइन और विश्लेषण करने के लिए हुक के नियम का उपयोग करते हैं।"
                    : "We use Hooke's Law to design and analyze elastic mechanisms across modern engineering—such as vehicle suspension systems, precision weighing scales, shock absorbers, and building seismology dampers. It allows engineers to predict how materials stretch and store mechanical potential energy under load."
            });
            _questions.Add(new QuestionEntry
            {
                id = "q3",
                textEn = "What is the formula for Hooke's Law?",
                textHi = "हुक के नियम का सूत्र क्या है?",
                tagEn = "Mathematical Formula",
                tagHi = "गणितीय सूत्र",
                getAnswer = lang => lang == "hi"
                    ? "हुक के नियम का सूत्र है F = -kx। यहाँ F न्यूटन में प्रत्यानयन बल है, x मीटर में विस्थापन या विस्तार है, और k न्यूटन प्रति मीटर में स्प्रिंग स्थिरांक है।"
                    : "The formula is F restoring equals minus k times x. F is the restoring force in Newtons, x is the displacement or extension in metres, and k is the spring constant in Newtons per metre. The negative sign signifies that the spring's restoring force always acts opposite to displacement."
            });
            _questions.Add(new QuestionEntry
            {
                id = "q4",
                textEn = "What is the spring constant?",
                textHi = "स्प्रिंग स्थिरांक (Spring Constant) क्या है?",
                tagEn = "Stiffness Parameter (k)",
                tagHi = "कड़ापन मापदंड (k)",
                getAnswer = lang => lang == "hi"
                    ? "स्प्रिंग स्थिरांक जिसे k से दर्शाया जाता है, स्प्रिंग के कड़ेपन की माप है। हमारी प्रयोगशाला में इस स्प्रिंग का मान ठीक 25.0 न्यूटन प्रति मीटर है।"
                    : "The spring constant, denoted as k, is a measure of the stiffness of a spring. It represents the amount of force in Newtons required to produce an extension of one metre. In our laboratory right now, this helical spring is calibrated to exactly 25.0 Newtons per metre."
            });
            _questions.Add(new QuestionEntry
            {
                id = "q5",
                textEn = "What happens when we add more mass?",
                textHi = "जब हम अधिक द्रव्यमान जोड़ते हैं तो क्या होता है?",
                tagEn = "Live Simulation Dynamics",
                tagHi = "लाइव सिमुलेशन गतिशीलता",
                getAnswer = lang =>
                {
                    SpringState s = _springSim != null ? _springSim.GetState() : default;
                    float forceN = (s.massG / 1000f) * (_springSim != null ? _springSim.gravity : 9.81f);
                    return lang == "hi"
                        ? $"जब आप हैंगर पर द्रव्यमान लटकाते हैं, तो गुरुत्वाकर्षण बल mg नीचे की ओर खिंचाव उत्पन्न करता है। अभी टेबल पर {s.massG} ग्राम भार लटका है, जो {forceN:F2} न्यूटन बल लगाकर {s.displacementCm:F1} सेंटीमीटर का विस्तार दे रहा है!"
                        : $"When you add mass onto the hanger, gravity exerts a downward force equal to mass times gravity. Currently on our table, there is {s.massG} grams loaded, exerting {forceN:F2} Newtons of downward gravitational force and producing an extension of {s.displacementCm:F1} centimetres!";
                }
            });
            _questions.Add(new QuestionEntry
            {
                id = "q6",
                textEn = "Force & displacement relationship?",
                textHi = "बल और विस्थापन में क्या संबंध है?",
                tagEn = "Linear Proportionality",
                tagHi = "रैखिक समानुपात",
                getAnswer = lang => lang == "hi"
                    ? "प्रत्यास्थता सीमा के भीतर बल और विस्थापन में सीधा रैखिक संबंध होता है। यही रैखिक संबंध दीवार पर लगी टीवी स्क्रीन पर एक सीधी रेखा का ग्राफ बनाता है जिसकी ढलान k देती है।"
                    : "Force and displacement have a direct linear relationship within the elastic limit. If you double the hanging weight, the spring extension doubles. This linear relationship produces the straight line with constant slope k that you can observe on the wall-mounted television graph."
            });
            _questions.Add(new QuestionEntry
            {
                id = "q7",
                textEn = "How do we calculate spring constant?",
                textHi = "हम स्प्रिंग स्थिरांक की गणना कैसे करते हैं?",
                tagEn = "Equilibrium Calculation",
                tagHi = "संतुलन गणना",
                getAnswer = lang =>
                {
                    SpringState s = _springSim != null ? _springSim.GetState() : default;
                    if (s.massG > 0 && s.displacementM > 0.005f)
                    {
                        float forceN = (s.massG / 1000f) * (_springSim != null ? _springSim.gravity : 9.81f);
                        float expK = forceN / s.displacementM;
                        return lang == "hi"
                            ? $"स्थैतिक संतुलन पर k = F/x होता है। हमारे वर्तमान {s.massG} ग्राम भार ({forceN:F2} N) और {s.displacementCm:F1} सेमी विस्तार के साथ, हमारा प्रायोगिक स्प्रिंग स्थिरांक {expK:F1} N/m आता है!"
                            : $"At static equilibrium, downward gravity m g equals upward restoring force k x. Therefore, k equals F applied divided by extension x. With our current load of {s.massG} grams ({forceN:F2} N) and extension of {s.displacementCm:F1} cm, our experimental k calculates to {expK:F1} N/m!";
                    }
                    return lang == "hi"
                        ? "स्थैतिक संतुलन पर गुरुत्वाकर्षण बल mg स्प्रिंग बल kx के बराबर होता है। इसलिए k = F / x। इसे परखने के लिए स्प्रिंग पर कोई भार लटकाएं!"
                        : "At static equilibrium, downward gravity m g equals upward restoring force k x. Therefore, k equals F applied divided by extension x. You can hang a calibration mass on the spring hanger to test this live!";
                }
            });
            _questions.Add(new QuestionEntry
            {
                id = "q8",
                textEn = "Elastic limit vs. breaking point?",
                textHi = "प्रत्यास्थता सीमा और विभंजन बिंदु में अंतर?",
                tagEn = "Material Limits",
                tagHi = "सामग्री सीमाएं",
                getAnswer = lang => lang == "hi"
                    ? "प्रत्यास्थता सीमा वह अधिकतम सीमा है जहाँ तक खींचने के बाद स्प्रिंग वापस अपनी मूल लंबाई में लौट आती है। विभंजन बिंदु वह उच्च बिंदु है जहाँ स्प्रिंग टूट जाती है।"
                    : "The elastic limit is the maximum force or extension a spring can withstand and still return completely to its original unloaded length. If stretched beyond this limit, it suffers permanent plastic deformation. The breaking point is the even higher threshold where the spring material structurally fractures."
            });
        }

        private void BuildDialogUI(Vector3 dialogWorldPos, TMP_FontAsset fontAsset)
        {
            _dialogRoot = new GameObject("TeacherDialogUIGroup");
            _dialogRoot.transform.SetParent(transform, false);
            _dialogRoot.transform.position = dialogWorldPos;
            _dialogBasePos = dialogWorldPos;

            const int W = 1024;
            const int H = 768;

            Canvas canvas = LabUIHelper.CreateWorldCanvas(
                "TeacherDialogCanvas",
                _dialogRoot.transform,
                Vector3.zero,
                Quaternion.identity,
                0.96f,
                0.72f,
                W,
                H,
                interactive: true);

            // 1. Proximity Prompt View
            RectTransform prox = LabUIHelper.CreatePanel("ProximityCard", canvas.transform,
                new Vector2((W - 760f) * 0.5f, (H - 340f) * 0.5f), new Vector2(760f, 340f),
                new Color(0.035f, 0.082f, 0.157f, 0.96f), LabUIHelper.SubtleCyanBorder, 3f);
            _proximityView = prox.gameObject;

            LabUIHelper.CreateText("ProxTitle", prox, "Talk to your Physics Teacher",
                new Vector2(50f, 44f), new Vector2(660f, 48f), 36f, Color.white,
                TextAlignmentOptions.Left, FontStyles.Bold, fontAsset);
            LabUIHelper.CreateText("ProxSub", prox, "Physics Lab • Virtual Hooke's Law Mentor",
                new Vector2(50f, 98f), new Vector2(660f, 34f), 22f, new Color(0.75f, 0.82f, 0.90f),
                TextAlignmentOptions.Left, FontStyles.Normal, fontAsset);
            LabUIHelper.CreateText("ProxHint", prox, "Have questions about spring constant, force, or extension?",
                new Vector2(50f, 155f), new Vector2(660f, 34f), 20f, new Color(0.85f, 0.90f, 0.95f),
                TextAlignmentOptions.Left, FontStyles.Normal, fontAsset);

            LabUIHelper.CreateButton("BtnAskInitial", prox, "Ask a Question",
                new Vector2(50f, 220f), new Vector2(660f, 75f),
                new Color(0.012f, 0.412f, 0.631f), new Color(0.008f, 0.518f, 0.78f), new Color(0.055f, 0.647f, 0.914f),
                28f, OpenConversation, out _, fontAsset);

            // 2. Full Conversation Console Container
            RectTransform console = LabUIHelper.CreatePanel("ConversationConsole", canvas.transform,
                new Vector2(16f, 16f), new Vector2(W - 32f, H - 32f),
                new Color(0.024f, 0.059f, 0.118f, 0.98f), LabUIHelper.SubtleCyanBorder, 3f);
            _menuView = new GameObject("MenuContent");
            _menuView.transform.SetParent(console, false);
            RectTransform menuRt = _menuView.AddComponent<RectTransform>();
            menuRt.anchorMin = Vector2.zero; menuRt.anchorMax = Vector2.one;
            menuRt.offsetMin = Vector2.zero; menuRt.offsetMax = Vector2.zero;

            // Header bar (shared inside console)
            RectTransform hdr = LabUIHelper.CreatePanel("HeaderBar", console, Vector2.zero, new Vector2(W - 32f, 95f),
                new Color(0.055f, 0.133f, 0.251f, 1f), new Color(0f, 0.9f, 1f, 0.4f), 2f);

            LabUIHelper.CreateText("MentorTitle", hdr, "Physics Mentor — Physics Lab",
                new Vector2(30f, 16f), new Vector2(600f, 36f), 30f, Color.white,
                TextAlignmentOptions.Left, FontStyles.Bold, fontAsset);

            _telemetrySubText = LabUIHelper.CreateText("TelemetrySub", hdr, "LAB STATE: 0g LOAD • x: 0.0cm • k: 25.0 N/m",
                new Vector2(30f, 56f), new Vector2(620f, 26f), 18f, new Color(0f, 0.9f, 1f),
                TextAlignmentOptions.Left, FontStyles.Bold, fontAsset);

            LabUIHelper.CreateButton("BtnLang", hdr, "EN / HI",
                new Vector2(W - 260f, 20f), new Vector2(130f, 55f),
                new Color(0.055f, 0.165f, 0.278f), new Color(0.118f, 0.227f, 0.373f), new Color(0.008f, 0.518f, 0.78f),
                20f, ToggleLanguage, out _langBtnLabel, fontAsset);

            LabUIHelper.CreateButton("BtnClose", hdr, "✕",
                new Vector2(W - 115f, 20f), new Vector2(58f, 55f),
                new Color(0.498f, 0.114f, 0.114f), new Color(0.863f, 0.149f, 0.149f), new Color(0.937f, 0.267f, 0.267f),
                26f, CloseConversation, out _, fontAsset);

            // Question selection grid inside _menuView
            LabUIHelper.CreateText("PickPrompt", _menuView.transform, "SELECT A QUESTION TO EXPLORE:",
                new Vector2(32f, 112f), new Vector2(700f, 32f), 22f, new Color(0.58f, 0.639f, 0.722f),
                TextAlignmentOptions.Left, FontStyles.Bold, fontAsset);

            _questionLabels.Clear();
            const float colW = 450f;
            const float btnH = 92f;
            const float gapX = 28f;
            const float gapY = 14f;
            for (int i = 0; i < _questions.Count; i++)
            {
                QuestionEntry q = _questions[i];
                int col = i % 2;
                int row = i / 2;
                float bx = 32f + col * (colW + gapX);
                float by = 152f + row * (btnH + gapY);

                QuestionEntry captured = q;
                LabUIHelper.CreateButton($"BtnQ_{i}", _menuView.transform, $"0{i + 1}. {q.textEn}",
                    new Vector2(bx, by), new Vector2(colW, btnH),
                    new Color(0.059f, 0.09f, 0.165f), new Color(0.118f, 0.161f, 0.231f), new Color(0.2f, 0.255f, 0.333f),
                    17f, () => AskQuestion(captured), out TMP_Text lbl, fontAsset);
                lbl.alignment = TextAlignmentOptions.Left;
                _questionLabels.Add(lbl);
            }

            // Custom Question Input + Ask button at bottom of _menuView
            BuildCustomInputRow(_menuView.transform, W - 32f, H - 32f, fontAsset);

            // 3. Answer View inside console
            _answerView = new GameObject("AnswerView");
            _answerView.transform.SetParent(console, false);
            RectTransform ansRt = _answerView.AddComponent<RectTransform>();
            ansRt.anchorMin = Vector2.zero; ansRt.anchorMax = Vector2.one;
            ansRt.offsetMin = Vector2.zero; ansRt.offsetMax = Vector2.zero;

            RectTransform qHeader = LabUIHelper.CreatePanel("QHeader", _answerView.transform,
                new Vector2(32f, 120f), new Vector2(W - 96f, 64f),
                new Color(0.059f, 0.09f, 0.165f, 1f), new Color(0.22f, 0.74f, 0.97f, 1f), 2f);
            _answerQuestionText = LabUIHelper.CreateText("QText", qHeader, "QUESTION:",
                new Vector2(18f, 12f), new Vector2(W - 132f, 40f), 21f, Color.white,
                TextAlignmentOptions.Left, FontStyles.Bold, fontAsset);

            RectTransform ansBox = LabUIHelper.CreatePanel("AnswerBox", _answerView.transform,
                new Vector2(32f, 202f), new Vector2(W - 96f, 405f),
                new Color(0.059f, 0.09f, 0.165f, 0.96f), new Color(0f, 0.9f, 1f, 1f), 3f);

            _speakingBadgeText = LabUIHelper.CreateText("SpeakingBadge", ansBox, "PHYSICS MENTOR IS SPEAKING...",
                new Vector2(24f, 16f), new Vector2(560f, 30f), 17f, new Color(0f, 0.9f, 1f),
                TextAlignmentOptions.Left, FontStyles.Bold, fontAsset);

            for (int b = 0; b < _waveformBars.Length; b++)
            {
                RectTransform barRt = LabUIHelper.CreatePanel($"Wave_{b}", ansBox,
                    new Vector2(W - 280f + b * 12f, 22f), new Vector2(7f, 20f), new Color(0f, 0.9f, 1f));
                _waveformBars[b] = barRt.GetComponent<Image>();
            }

            _answerBodyText = LabUIHelper.CreateText("AnswerBody", ansBox, "",
                new Vector2(24f, 64f), new Vector2(W - 144f, 325f), 24f, new Color(0.973f, 0.98f, 0.988f),
                TextAlignmentOptions.TopLeft, FontStyles.Normal, fontAsset);

            LabUIHelper.CreateButton("BtnStop", _answerView.transform, "■ Stop Speaking",
                new Vector2(32f, H - 115f), new Vector2(450f, 65f),
                new Color(0.6f, 0.106f, 0.106f), new Color(0.725f, 0.11f, 0.11f), new Color(0.937f, 0.267f, 0.267f),
                22f, StopSpeaking, out _, fontAsset);

            LabUIHelper.CreateButton("BtnBack", _answerView.transform, "← More Questions",
                new Vector2(510f, H - 115f), new Vector2(450f, 65f),
                new Color(0.012f, 0.412f, 0.631f), new Color(0.008f, 0.518f, 0.78f), new Color(0.055f, 0.647f, 0.914f),
                22f, BackToMenu, out _, fontAsset);

            _dialogRoot.SetActive(false);
        }

        private void BuildCustomInputRow(Transform parent, float panelW, float panelH, TMP_FontAsset fontAsset)
        {
            float barY = panelH - 86f;
            RectTransform inputBg = LabUIHelper.CreatePanel("CustomInputBg", parent, new Vector2(32f, barY),
                new Vector2(panelW - 264f, 62f), new Color(0.059f, 0.09f, 0.165f), new Color(0.388f, 0.4f, 0.945f), 2f);

            GameObject textArea = new GameObject("TextArea");
            textArea.transform.SetParent(inputBg, false);
            RectTransform tart = textArea.AddComponent<RectTransform>();
            tart.anchorMin = Vector2.zero; tart.anchorMax = Vector2.one;
            tart.offsetMin = new Vector2(14f, 6f); tart.offsetMax = new Vector2(-14f, -6f);

            TMP_Text placeholder = LabUIHelper.CreateText("Placeholder", textArea.transform,
                "Type a custom question (e.g. formula, spring constant, elastic limit)...",
                Vector2.zero, new Vector2(panelW - 300f, 48f), 19f, new Color(0.58f, 0.639f, 0.722f),
                TextAlignmentOptions.MidlineLeft, FontStyles.Italic, fontAsset);

            GameObject txtGo = new GameObject("Text");
            txtGo.transform.SetParent(textArea.transform, false);
            RectTransform trt = txtGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            TMP_Text inputText = txtGo.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) inputText.font = fontAsset;
            inputText.fontSize = 20f;
            inputText.color = Color.white;
            inputText.alignment = TextAlignmentOptions.MidlineLeft;

            _customInputField = inputBg.gameObject.AddComponent<TMP_InputField>();
            _customInputField.textViewport = tart;
            _customInputField.textComponent = inputText;
            _customInputField.placeholder = placeholder;
            _customInputField.onSubmit.AddListener(HandleCustomQuestion);

            LabUIHelper.CreateButton("BtnAskCustom", parent, "ASK",
                new Vector2(panelW - 216f, barY), new Vector2(184f, 62f),
                new Color(0.192f, 0.18f, 0.506f), new Color(0.263f, 0.22f, 0.792f), new Color(0.506f, 0.549f, 0.973f),
                22f, () =>
                {
                    if (_customInputField != null)
                    {
                        HandleCustomQuestion(_customInputField.text);
                        _customInputField.text = "";
                    }
                }, out _, fontAsset);
        }

        public void SetState(TeacherConversationState newState)
        {
            State = newState;
            if (_dialogRoot == null) return;

            if (newState == TeacherConversationState.Idle || newState == TeacherConversationState.ConversationClosed)
            {
                StopSpeakingInternal();
                _dialogRoot.SetActive(false);
                _activeQuestion = null;
                _activeAnswer = "";
            }
            else
            {
                _dialogRoot.SetActive(true);
                RefreshUI();
            }
        }

        public void OpenConversation()
        {
            SetState(TeacherConversationState.ConversationOpen);
            OnTeacherHelpOpened?.Invoke();
        }

        public void CloseConversation()
        {
            SetState(TeacherConversationState.ConversationClosed);
            SetState(TeacherConversationState.Idle);
        }

        public void BackToMenu()
        {
            StopSpeakingInternal();
            _activeQuestion = null;
            _activeAnswer = "";
            SetState(TeacherConversationState.ConversationOpen);
        }

        public void ToggleLanguage()
        {
            _activeLanguage = _activeLanguage == "en" ? "hi" : "en";
            if (_activeQuestion != null && (State == TeacherConversationState.TeacherSpeaking || !string.IsNullOrEmpty(_activeAnswer)))
            {
                AskQuestion(_activeQuestion);
            }
            else
            {
                RefreshUI();
            }
        }

        public void AskQuestion(QuestionEntry q)
        {
            if (q == null) return;
            _activeQuestion = q;
            _activeAnswer = q.getAnswer != null ? q.getAnswer(_activeLanguage) : "";
            PlayLessonResponse(q.id, _activeAnswer);
        }

        public void HandleCustomQuestion(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return;
            string q = query.ToLowerInvariant().Trim();

            QuestionEntry matched = null;
            foreach (QuestionEntry sq in _questions)
            {
                if (q.Contains(sq.textEn.ToLowerInvariant()) || sq.textEn.ToLowerInvariant().Contains(q))
                {
                    matched = sq;
                    break;
                }
            }

            if (matched == null)
            {
                if (q.Contains("formula") || q.Contains("equation")) matched = _questions[2];
                else if (q.Contains("spring constant") || q.Contains("stiffness") || q.Contains("value of k")) matched = _questions[3];
                else if (q.Contains("more mass") || q.Contains("add mass") || q.Contains("weight") || q.Contains("load")) matched = _questions[4];
                else if (q.Contains("calculate") || q.Contains("find k") || q.Contains("slope")) matched = _questions[6];
                else if (q.Contains("elastic limit") || q.Contains("breaking") || q.Contains("break") || q.Contains("permanent")) matched = _questions[7];
                else if (q.Contains("why") || q.Contains("use") || q.Contains("application")) matched = _questions[1];
                else if (q.Contains("relationship") || q.Contains("graph") || q.Contains("linear")) matched = _questions[5];
                else if (q.Contains("hooke")) matched = _questions[0];
            }

            if (matched != null)
            {
                AskQuestion(matched);
                return;
            }

            _activeQuestion = new QuestionEntry { id = "", textEn = query, textHi = query };
            _activeAnswer = $"That is an insightful question about \"{query}\". In our Hooke's Law laboratory, spring behavior is governed by F = -kx. Force is proportional to displacement, meaning every increase in applied mass produces a proportional increase in extension!";
            PlayLessonResponse(null, _activeAnswer);
        }

        private void PlayLessonResponse(string lessonId, string answerText)
        {
            StopSpeakingInternal();
            _guideCoordinator?.NotifyTeacherSpeechStarting();
            OnTeacherLessonStarted?.Invoke();

            _activeAnswer = answerText;
            State = TeacherConversationState.TeacherSpeaking;
            _isSpeaking = true;

            if (!string.IsNullOrEmpty(lessonId) && _voiceSource != null)
            {
                AudioClip clip = Resources.Load<AudioClip>($"Teacher/{lessonId}-{_activeLanguage}");
                if (clip != null)
                {
                    _voiceSource.spatialBlend = 0.65f; // Spatialized teacher voice at podium
                    _voiceSource.outputAudioMixerGroup = null;
                    _voiceSource.bypassEffects = false;
                    _voiceSource.clip = clip;
                    if (_guideCoordinator != null) _voiceSource.volume = _guideCoordinator.VoiceVolume;
                    _voiceSource.Play();
                }
            }

            RefreshUI();
        }

        public void StopSpeaking()
        {
            StopSpeakingInternal();
            if (State == TeacherConversationState.TeacherSpeaking)
            {
                SetState(TeacherConversationState.ConversationOpen);
            }
        }

        private void StopSpeakingInternal()
        {
            bool wasSpeaking = _isSpeaking;
            if (_voiceSource != null && _voiceSource.isPlaying)
            {
                _voiceSource.Stop();
            }
            _isSpeaking = false;
            if (wasSpeaking)
            {
                _guideCoordinator?.NotifyTeacherSpeechEnded();
                OnTeacherLessonEnded?.Invoke();
            }
        }

        private void RefreshUI()
        {
            if (_dialogRoot == null) return;

            bool isNear = State == TeacherConversationState.NearTeacher;
            bool isAns = State == TeacherConversationState.TeacherSpeaking || (!string.IsNullOrEmpty(_activeAnswer) && State != TeacherConversationState.ConversationOpen);

            if (_proximityView != null) _proximityView.SetActive(isNear);
            if (_menuView != null) _menuView.transform.parent.gameObject.SetActive(!isNear);
            if (_menuView != null) _menuView.SetActive(!isNear && !isAns);
            if (_answerView != null) _answerView.SetActive(!isNear && isAns);

            SpringState s = _springSim != null ? _springSim.GetState() : default;
            if (_telemetrySubText != null)
            {
                _telemetrySubText.text = $"LAB STATE: {s.massG}g LOAD • x: {s.displacementCm:F1}cm • k: {(_springSim != null ? _springSim.springConstant : 25f):F1} N/m";
            }

            if (_langBtnLabel != null)
            {
                _langBtnLabel.text = _activeLanguage == "hi" ? "HI (हिंदी)" : "EN (ENG)";
            }

            for (int i = 0; i < _questions.Count && i < _questionLabels.Count; i++)
            {
                QuestionEntry q = _questions[i];
                string qTxt = _activeLanguage == "hi" ? q.textHi : q.textEn;
                string tagTxt = _activeLanguage == "hi" ? q.tagHi : q.tagEn;
                _questionLabels[i].text = $"<color=#38bdf8>0{i + 1}.</color> {qTxt}\n<size=14><color=#94a3b8>{tagTxt}</color></size>";
            }

            if (_answerQuestionText != null && _activeQuestion != null)
            {
                string qt = _activeLanguage == "hi" && !string.IsNullOrEmpty(_activeQuestion.textHi) ? _activeQuestion.textHi : _activeQuestion.textEn;
                _answerQuestionText.text = $"<color=#38bdf8>QUESTION:</color>  {qt}";
            }
            if (_answerBodyText != null) _answerBodyText.text = _activeAnswer;
            if (_speakingBadgeText != null)
            {
                _speakingBadgeText.text = _isSpeaking ? "PHYSICS MENTOR IS SPEAKING..." : "EXPLANATION COMPLETE";
                _speakingBadgeText.color = _isSpeaking ? new Color(0f, 0.9f, 1f) : new Color(0.063f, 0.725f, 0.506f);
            }
        }

        public void TickUpdate(float deltaTime, Vector3 playerHeadWorldPos)
        {
            _animTime += deltaTime;

            // Check if voice clip finished playing
            if (State == TeacherConversationState.TeacherSpeaking && _isSpeaking && _voiceSource != null)
            {
                if (_voiceSource.clip != null && !_voiceSource.isPlaying)
                {
                    _isSpeaking = false;
                    _guideCoordinator?.NotifyTeacherSpeechEnded();
                    OnTeacherLessonEnded?.Invoke();
                    RefreshUI();
                }
            }

            // Proximity detection (horizontal XZ distance)
            float dist = Vector2.Distance(
                new Vector2(_teacherBasePos.x, _teacherBasePos.z),
                new Vector2(playerHeadWorldPos.x, playerHeadWorldPos.z));

            if (dist <= activationDistance)
            {
                if (State == TeacherConversationState.Idle)
                {
                    SetState(TeacherConversationState.NearTeacher);
                }
            }
            else if (dist >= deactivationDistance)
            {
                if (State != TeacherConversationState.Idle)
                {
                    CloseConversation();
                }
            }

            // Procedural breathing, nod, and gaze
            float breathingOffset = Mathf.Sin(_animTime * 1.6f) * 0.0018f;
            float speakingNodDeg = _isSpeaking ? Mathf.Sin(_animTime * 4.5f) * 1.3f : 0f;
            _explanatoryLean = Mathf.Lerp(_explanatoryLean, _isSpeaking ? 0.9f : 0f, 0.08f);

            if (_teacherRig != null)
            {
                _teacherRig.position = new Vector3(_teacherBasePos.x, _teacherBasePos.y + breathingOffset, _teacherBasePos.z);

                if (dist < 4.0f)
                {
                    Vector3 toPlayer = playerHeadWorldPos - _teacherBasePos;
                    toPlayer.y = 0f;
                    if (toPlayer.sqrMagnitude > 0.01f)
                    {
                        float yaw = Vector3.SignedAngle(Vector3.forward, toPlayer.normalized, Vector3.up);
                        yaw = Mathf.Clamp(yaw * 0.25f, -24f, 24f);
                        Quaternion targetRot = Quaternion.Euler(speakingNodDeg, yaw, -_explanatoryLean);
                        _teacherRig.localRotation = Quaternion.Slerp(_teacherRig.localRotation, targetRot, 0.05f);
                    }
                }
            }

            // Floating dialogue animation & gentle billboard toward player
            if (_dialogRoot != null && _dialogRoot.activeSelf)
            {
                _dialogRoot.transform.position = _dialogBasePos + new Vector3(0f, Mathf.Sin(_animTime * 1.5f) * 0.012f, 0f);
                Vector3 dir = _dialogRoot.transform.position - playerHeadWorldPos;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                {
                    Quaternion look = Quaternion.LookRotation(dir.normalized, Vector3.up);
                    _dialogRoot.transform.rotation = Quaternion.Slerp(_dialogRoot.transform.rotation, look, 0.06f);
                }

                if (_isSpeaking)
                {
                    for (int b = 0; b < _waveformBars.Length; b++)
                    {
                        if (_waveformBars[b] == null) continue;
                        float h = 8f + Mathf.Abs(Mathf.Sin(_animTime * 6f + b * 0.6f)) * 24f;
                        _waveformBars[b].rectTransform.sizeDelta = new Vector2(7f, h);
                    }
                }
            }
        }
    }
}
