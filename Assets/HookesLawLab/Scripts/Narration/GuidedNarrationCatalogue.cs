using System;
using System.Collections.Generic;
using UnityEngine;

namespace HookesLawLab.Narration
{
    public enum NarrationPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }

    public enum NarrationReplayRule
    {
        OncePerStep = 0,
        OncePerSession = 1,
        Repeatable = 2,
        Debounced = 3
    }

    [Serializable]
    public class NarrationEntry
    {
        public string id;
        public AudioClip clip;
        [TextArea(2, 5)]
        public string subtitle;
        public string language = "en";
        public NarrationPriority priority = NarrationPriority.Normal;
        public NarrationReplayRule replayRule = NarrationReplayRule.OncePerStep;
        public float minCooldownSeconds = 3.0f;
        public float fallbackDurationSeconds = 5.0f;
    }

    /// <summary>
    /// Serialized catalogue of all guided voice-narration IDs, local AudioClips, subtitles,
    /// language, priority, and replay rules for offline Meta Quest 3S playback.
    /// </summary>
    [CreateAssetMenu(fileName = "GuidedNarrationCatalogue", menuName = "Physics Lab/Guided Narration Catalogue")]
    public class GuidedNarrationCatalogue : ScriptableObject
    {
        public List<NarrationEntry> entries = new List<NarrationEntry>();

        private Dictionary<string, NarrationEntry> _lookup;

        public void RebuildLookup()
        {
            _lookup = new Dictionary<string, NarrationEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (NarrationEntry e in entries)
            {
                if (e != null && !string.IsNullOrEmpty(e.id) && !_lookup.ContainsKey(e.id))
                {
                    _lookup.Add(e.id, e);
                }
            }
        }

        public NarrationEntry GetEntry(string id)
        {
            if (_lookup == null) RebuildLookup();
            if (!string.IsNullOrEmpty(id) && _lookup.TryGetValue(id, out NarrationEntry entry))
            {
                return entry;
            }
            return null;
        }

        public static bool IsPlayableClip(AudioClip clip)
        {
            return clip != null && clip.length > 0.01f && clip.loadState != AudioDataLoadState.Failed;
        }

        public AudioClip ResolveClip(string id)
        {
            NarrationEntry entry = GetEntry(id);
            if (entry == null) return null;

            if (IsPlayableClip(entry.clip))
            {
                return entry.clip;
            }

            AudioClip loaded = Resources.Load<AudioClip>($"Guide/{entry.id}");
            if (IsPlayableClip(loaded))
            {
                entry.clip = loaded;
                return loaded;
            }

            return null;
        }

        public NarrationEntry GetFirstPlayableEntry()
        {
            foreach (NarrationEntry e in entries)
            {
                if (e != null && ResolveClip(e.id) != null)
                {
                    return e;
                }
            }
            return null;
        }

        public List<string> GetMissingClipIds()
        {
            var missing = new List<string>();
            foreach (NarrationEntry e in entries)
            {
                if (e != null && ResolveClip(e.id) == null)
                {
                    missing.Add(e.id);
                }
            }
            return missing;
        }

        public static GuidedNarrationCatalogue CreateDefaultRuntimeCatalogue()
        {
            GuidedNarrationCatalogue cat = CreateInstance<GuidedNarrationCatalogue>();
            PopulateDefaultEntries(cat);
            return cat;
        }

        public static void PopulateDefaultEntries(GuidedNarrationCatalogue cat)
        {
            if (cat == null) return;

            var existingClips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
            foreach (NarrationEntry old in cat.entries)
            {
                if (old != null && !string.IsNullOrEmpty(old.id) && old.clip != null)
                {
                    existingClips[old.id] = old.clip;
                }
            }

            cat.entries.Clear();

            void Add(string id, string subtitle, NarrationPriority priority, NarrationReplayRule rule, float cooldown = 3.0f)
            {
                AudioClip clip = null;
                if (existingClips.TryGetValue(id, out AudioClip prev))
                {
                    clip = prev;
                }
                else
                {
                    clip = Resources.Load<AudioClip>($"Guide/{id}");
                }

                int words = string.IsNullOrEmpty(subtitle) ? 4 : subtitle.Split(' ').Length;
                float estDuration = Mathf.Clamp(words * 0.42f, 3.0f, 18.0f);

                cat.entries.Add(new NarrationEntry
                {
                    id = id,
                    clip = clip,
                    subtitle = subtitle,
                    language = "en",
                    priority = priority,
                    replayRule = rule,
                    minCooldownSeconds = cooldown,
                    fallbackDurationSeconds = estDuration
                });
            }

            Add("welcome",
                "Welcome to the Virtual Physics Lab. Today, we will explore Hooke's Law and discover how a spring stretches when we add weight. Look around, then use point teleportation to move towards the experiment table.",
                NarrationPriority.Normal, NarrationReplayRule.OncePerSession);

            Add("movement",
                "Point your thumbstick forward toward the floor and release when the teleport path turns blue-cyan to move to the experiment table.",
                NarrationPriority.Normal, NarrationReplayRule.OncePerStep);

            Add("apparatus",
                "This is the spring experiment. You can attach different masses, measure the spring's extension, and record your results. Select Earth's gravity to begin.",
                NarrationPriority.Normal, NarrationReplayRule.OncePerStep);

            Add("hookes_law",
                "Hooke's Law states that a spring's restoring force is proportional to its extension within its elastic limit: F equals minus k x.",
                NarrationPriority.Normal, NarrationReplayRule.OncePerStep);

            Add("pickup_prompt",
                "Pick up a weight using the grip button on your controller. Start with the smallest available mass.",
                NarrationPriority.Normal, NarrationReplayRule.OncePerStep);

            Add("pickup_success",
                "Move the weight towards the spring's attachment point. Release it when it is inside the attachment area.",
                NarrationPriority.High, NarrationReplayRule.OncePerStep);

            Add("attach_prompt",
                "The weight is inside the attachment area. Release the grip button to attach it to the spring.",
                NarrationPriority.High, NarrationReplayRule.Debounced, 2.5f);

            Add("attach_success",
                "Well done. The weight pulls the spring down. Wait for the spring to settle before taking a measurement.",
                NarrationPriority.High, NarrationReplayRule.OncePerStep);

            Add("return_to_table",
                "The weight returned to the table. Pick it up again using the grip button, hold it in the attachment area, and release.",
                NarrationPriority.Normal, NarrationReplayRule.Debounced, 3.0f);

            Add("equilibrium",
                "Wait for the oscillation to settle, or select Settle Spring for static equilibrium.",
                NarrationPriority.Normal, NarrationReplayRule.OncePerStep);

            Add("record_prompt",
                "Now observe the extension reading. Extension is the increase in length from the spring's unloaded position. Select Record Point to save your measurement.",
                NarrationPriority.Normal, NarrationReplayRule.OncePerStep);

            Add("record_success",
                "Your measurement has been recorded. Detach the weight to return it to the table, then try a different mass. Keep the gravity and spring stiffness unchanged while comparing your results.",
                NarrationPriority.High, NarrationReplayRule.OncePerStep);

            Add("record_rejected_moving",
                "Recording failed because the spring is still oscillating. Wait for equilibrium or select Settle Spring, then record again.",
                NarrationPriority.High, NarrationReplayRule.Debounced, 2.5f);

            Add("record_rejected_empty",
                "Recording failed because no mass is attached. Attach a weight and let the spring settle before recording.",
                NarrationPriority.High, NarrationReplayRule.Debounced, 2.5f);

            Add("try_different_loads",
                "Detach the current weight, attach another mass, and record at least three different total masses at the same gravity and spring stiffness.",
                NarrationPriority.Normal, NarrationReplayRule.OncePerStep);

            Add("graph_explanation",
                "Look at the force-extension graph. Within the elastic limit, greater force produces greater extension. For force plotted vertically against extension horizontally, the slope represents the spring constant.",
                NarrationPriority.Normal, NarrationReplayRule.OncePerStep);

            Add("gravity_prompt",
                "Now keep the same mass attached and select another planet. Compare how the spring's extension changes with gravity.",
                NarrationPriority.Normal, NarrationReplayRule.OncePerStep);

            Add("gravity_earth",
                "Earth's gravity is selected. Observe the spring's extension.",
                NarrationPriority.High, NarrationReplayRule.Repeatable, 1.5f);

            Add("gravity_moon",
                "The Moon has weaker gravity than Earth. With the same mass and spring stiffness, the equilibrium extension is smaller.",
                NarrationPriority.High, NarrationReplayRule.Repeatable, 1.5f);

            Add("gravity_mars",
                "Mars has weaker gravity than Earth. With the same mass and spring stiffness, the equilibrium extension is smaller.",
                NarrationPriority.High, NarrationReplayRule.Repeatable, 1.5f);

            Add("gravity_jupiter",
                "Jupiter has stronger gravity than Earth. With the same mass and spring stiffness, the predicted extension is larger, provided the spring remains within its elastic range.",
                NarrationPriority.High, NarrationReplayRule.Repeatable, 1.5f);

            Add("elastic_limit",
                "The extension has exceeded the experiment's elastic-limit threshold. Reduce the load so the spring stays within its elastic range.",
                NarrationPriority.Critical, NarrationReplayRule.Debounced, 5.0f);

            Add("teacher_help",
                "You can now visit the virtual teacher to ask questions about Hooke's Law. You can also read the theory board and select YouTube to watch the linked educational video.",
                NarrationPriority.Normal, NarrationReplayRule.OncePerStep);

            Add("completion",
                "You have completed the guided experiment. You explored how mass and gravity affect spring extension and recorded your results. You can continue experimenting and exploring the laboratory.",
                NarrationPriority.High, NarrationReplayRule.OncePerStep);

            cat.RebuildLookup();
        }
    }
}
