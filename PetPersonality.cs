using System;
using System.Collections.Generic;
using System.Linq;

namespace KeyMapper
{
    internal enum PetAction
    {
        Command,
        DeGibberish,
        Translate,
        Ocr,
        WalkingOn,
        WalkingOff,
        Settings
    }

    internal sealed record ForegroundContext(string ProcessName, string WindowTitle)
    {
        public bool IsBrowser =>
            ProcessName.Contains("chrome", StringComparison.OrdinalIgnoreCase) ||
            ProcessName.Contains("msedge", StringComparison.OrdinalIgnoreCase) ||
            ProcessName.Contains("firefox", StringComparison.OrdinalIgnoreCase) ||
            ProcessName.Contains("samsunginternet", StringComparison.OrdinalIgnoreCase);

        public bool IsCoding =>
            ProcessName.Contains("devenv", StringComparison.OrdinalIgnoreCase) ||
            ProcessName.Contains("code", StringComparison.OrdinalIgnoreCase) ||
            ProcessName.Contains("rider", StringComparison.OrdinalIgnoreCase) ||
            WindowTitle.Contains("Visual Studio", StringComparison.OrdinalIgnoreCase) ||
            WindowTitle.Contains("GitHub", StringComparison.OrdinalIgnoreCase);

        public bool IsChat =>
            ProcessName.Contains("telegram", StringComparison.OrdinalIgnoreCase) ||
            ProcessName.Contains("discord", StringComparison.OrdinalIgnoreCase) ||
            ProcessName.Contains("whatsapp", StringComparison.OrdinalIgnoreCase);

        public bool IsVideo =>
            WindowTitle.Contains("YouTube", StringComparison.OrdinalIgnoreCase) ||
            WindowTitle.Contains("video", StringComparison.OrdinalIgnoreCase);

        public string Topic
        {
            get
            {
                string title = WindowTitle.Trim();
                int separator = title.LastIndexOf(" - ", StringComparison.Ordinal);
                if (separator > 0) title = title[..separator];
                return title.Length > 52 ? $"{title[..49]}…" : title;
            }
        }
    }

    internal sealed class PetPersonalityProfile
    {
        private readonly IReadOnlyDictionary<PetAction, string[]> _actionLines;
        private readonly IReadOnlyDictionary<int, string[]> _hourlyComments;
        private readonly Dictionary<string, Queue<string>> _lineBags =
            new(StringComparer.Ordinal);
        private string _lastLine = string.Empty;

        public string CharacterName { get; }
        public string SpeakerName { get; }
        public double MovementMultiplier { get; }
        public int MinimumPauseSeconds { get; }
        public int MaximumPauseSeconds { get; }
        public int ObservationCooldownSeconds { get; }
        public string[] Introductions { get; }
        public string[] BrowserObservations { get; }
        public string[] CodingObservations { get; }
        public string[] ChatObservations { get; }
        public string[] VideoObservations { get; }
        public string[] MusicObservations { get; }
        public string[] GeneralObservations { get; }
        public string[] BreakReminders { get; }
        public int SarcasmChancePercent { get; }

        public PetPersonalityProfile(
            string characterName,
            string speakerName,
            double movementMultiplier,
            int minimumPauseSeconds,
            int maximumPauseSeconds,
            int observationCooldownSeconds,
            string[] introductions,
            string[] browserObservations,
            string[] codingObservations,
            string[] chatObservations,
            string[] videoObservations,
            string[] musicObservations,
            string[] generalObservations,
            string[] breakReminders,
            IReadOnlyDictionary<PetAction, string[]> actionLines)
        {
            CharacterName = characterName;
            SpeakerName = speakerName;
            MovementMultiplier = movementMultiplier;
            MinimumPauseSeconds = minimumPauseSeconds;
            MaximumPauseSeconds = maximumPauseSeconds;
            ObservationCooldownSeconds = observationCooldownSeconds;
            Introductions = introductions;
            BrowserObservations = browserObservations;
            CodingObservations = codingObservations;
            ChatObservations = chatObservations;
            VideoObservations = videoObservations;
            MusicObservations = musicObservations;
            GeneralObservations = generalObservations;
            BreakReminders = breakReminders;
            _actionLines = actionLines;
            _hourlyComments = CreateHourlyComments(characterName);
            SarcasmChancePercent = characterName switch
            {
                "Frieren" => 8,
                "Owlet Monster" => 12,
                "Yuji Itadori" => 18,
                "Monkey D. Luffy" => 24,
                "Dude Monster" => 34,
                _ => 26
            };
        }

        public string Introduction(Random random) =>
            Pick("introduction", Introductions, random);

        public string ActionLine(PetAction action, Random random)
        {
            return _actionLines.TryGetValue(action, out string[]? lines)
                ? Pick($"action:{action}", lines, random)
                : "Ready.";
        }

        public string Observation(ForegroundContext context, Random random)
        {
            string category;
            string[] lines;
            if (context.IsVideo)
            {
                category = "video";
                lines = VideoObservations;
            }
            else if (context.IsCoding)
            {
                category = "coding";
                lines = CodingObservations;
            }
            else if (context.IsChat)
            {
                category = "chat";
                lines = ChatObservations;
            }
            else if (context.IsBrowser)
            {
                category = "browser";
                lines = BrowserObservations;
            }
            else
            {
                category = "general";
                lines = GeneralObservations;
            }

            return Pick($"observation:{category}", lines, random)
                .Replace("{topic}", context.Topic);
        }

        public string BreakReminder(Random random) =>
            Pick("break", BreakReminders, random);

        public string MusicObservation(
            string title,
            string artist,
            Random random) =>
            Pick("music", MusicObservations, random)
                .Replace("{title}", title)
                .Replace("{artist}", artist);

        public string HourlyComment(int hour, Random random)
        {
            if (!_hourlyComments.TryGetValue(hour, out string[]? lines) || lines.Length == 0)
            {
                return string.Empty;
            }

            bool useSarcasm = SarcasmChancePercent > 0 &&
                              random.Next(100) < SarcasmChancePercent;
            string[] selectedLines = useSarcasm
                ? SarcasticLines(CharacterName)
                : lines;
            string key = useSarcasm ? "hourly:sarcasm" : $"hourly:{hour}";

            return Pick(key, selectedLines, random)
                .Replace("{time}", FormatHour(hour));
        }

        private static string FormatHour(int hour)
        {
            int normalized = ((hour % 24) + 24) % 24;
            string suffix = normalized >= 12 ? "PM" : "AM";
            int displayHour = normalized % 12;
            if (displayHour == 0) displayHour = 12;
            return $"{displayHour}:00 {suffix}";
        }

        private static string[] SarcasticLines(string characterName) =>
            characterName switch
            {
                "Pink Monster" =>
                [
                    "It is {time}. We have survived another hour of pretending that tab will organize itself.",
                    "At {time}, the desktop remains open and the task remains suspiciously unfinished. Impressive.",
                    "Another hour, another chance to call that one tiny task a quick win and then open three more tabs.",
                    "The clock says {time}. I checked. It still cannot finish the task for you. Rude."
                ],
                "Owlet Monster" =>
                [
                    "It is {time}. Your workflow appears to be conducting an experiment in how many tabs can coexist.",
                    "At {time}, I offer a gentle observation: the task is still waiting, despite the very confident tab switching.",
                    "Another hour has passed. The unfinished item remains remarkably committed to its position.",
                    "The clock says {time}. Perhaps the next plan could include actually starting the plan."
                ],
                "Dude Monster" =>
                [
                    "{time}. That task is still open? Bold strategy.",
                    "Another hour gone and the same tab is staring back. It is winning on points.",
                    "The clock hit {time}. Maybe that tiny task is not going to defeat itself.",
                    "At {time}, I have one suggestion: do the thing before opening another thing."
                ],
                "Frieren" =>
                [
                    "It is {time}. The unfinished task has waited patiently, which is more than most humans manage.",
                    "Another hour has passed. I suspect the tab is becoming a historical artifact.",
                    "At {time}, even an ancient mage might consider closing one of those windows.",
                    "The clock says {time}. Time is strange. So is keeping the same task untouched for so long."
                ],
                "Yuji Itadori" =>
                [
                    "It is {time}! That task is still standing? Fine, we can take it down together.",
                    "Another hour! The tab is undefeated, but I am not giving it the victory speech yet.",
                    "At {time}, let us finish one thing before the next thing jumps us.",
                    "The clock says {time}. I brought energy. The task should probably bring some too."
                ],
                "Monkey D. Luffy" =>
                [
                    "{time}! That task is still hiding? I will find it after I find some meat.",
                    "Another hour passed and the tab is still here. Is it part of the crew now?",
                    "At {time}, I say we finish this before the next adventure steals the whole day.",
                    "The clock says {time}. The task cannot run away forever. Probably."
                ],
                _ =>
                [
                    "It is {time}. One small task is still waiting for a hero.",
                    "Another hour has passed. Shall we make one useful dent in the list?"
                ]
            };

        private static IReadOnlyDictionary<int, string[]> CreateHourlyComments(string characterName) =>
            characterName switch
            {
                "Pink Monster" => new Dictionary<int, string[]>
                {
                    [8] =
                    [
                        "Good morning! It is {time}, and I have already spotted at least one tiny win hiding nearby.",
                        "Morning patrol at {time}. Let us make the first task smaller than it looks.",
                        "The day is awake at {time}. I brought quick feet and a short list of useful ideas."
                    ],
                    [13] =
                    [
                        "It is {time}. Midday checkpoint: one sip of water, one useful shortcut, then back to it.",
                        "Lunch-hour patrol at {time}. I can turn a repeated phrase into an expansion if you want.",
                        "Half the day is doing its little sprint. What should we rescue before evening?"
                    ],
                    [18] =
                    [
                        "Evening check at {time}. The desktop is calmer now, so the tricky task has nowhere to hide.",
                        "It is {time}. Good time for a tidy finish and one satisfying checkbox.",
                        "The day changed color at {time}. I am voting for a small win before the next scroll."
                    ],
                    [22] =
                    [
                        "Night patrol at {time}. Save the important work before the pixels start yawning.",
                        "It is {time}. We can finish one gentle task, then let tomorrow inherit the rest.",
                        "Late-hour idea at {time}: write down the next step so future-you does not have to hunt for it."
                    ]
                },
                "Owlet Monster" => new Dictionary<int, string[]>
                {
                    [8] =
                    [
                        "Good morning. At {time}, a concise plan will serve us better than a heroic scramble.",
                        "Morning observation at {time}: choose one task, define its finish line, and begin there.",
                        "The day begins at {time}. A clear desk and a clear first step are sufficient."
                    ],
                    [13] =
                    [
                        "It is {time}. A measured pause may improve the quality of the next decision.",
                        "Midday review at {time}: which open task is closest to a useful conclusion?",
                        "At {time}, consider saving a small checkpoint before changing direction."
                    ],
                    [18] =
                    [
                        "Evening at {time}. A short review now can prevent tomorrow from beginning with archaeology.",
                        "It is {time}. Finish one clear thread before opening another, if possible.",
                        "The workday is softening at {time}. This is a good moment to capture the next action."
                    ],
                    [22] =
                    [
                        "At {time}, preserve your progress and allow the mind a quieter interval.",
                        "Night note at {time}: unfinished work is easier to resume when its next step is written down.",
                        "It is {time}. Even a careful system benefits from closing a few loops."
                    ]
                },
                "Dude Monster" => new Dictionary<int, string[]>
                {
                    [8] =
                    [
                        "{time}. Pick the first useful task and hit it before the inbox starts spawning copies.",
                        "Morning check at {time}: one clear goal beats ten vague intentions.",
                        "It is {time}. Save the dramatic plan for later and do the small obvious thing first."
                    ],
                    [13] =
                    [
                        "Midday at {time}. Water, stretch, then finish the task that is closest to done.",
                        "{time}. Quick checkpoint: what can we close instead of merely reopening?",
                        "Lunch-hour reality check at {time}: the shortcut is probably shorter than retyping everything."
                    ],
                    [18] =
                    [
                        "{time}. Wrap one thing cleanly before the day starts charging interest.",
                        "Evening check at {time}: save, test, close the loop. Easy win.",
                        "The day is nearly done at {time}. Do not let the last task become tomorrow's boss fight."
                    ],
                    [22] =
                    [
                        "{time}. Back up the important stuff and stop negotiating with the same unfinished task.",
                        "Late shift at {time}: write the next step, then log off before the tabs unionize.",
                        "It is {time}. Good work. Now leave a breadcrumb for tomorrow."
                    ]
                },
                "Frieren" => new Dictionary<int, string[]>
                {
                    [8] =
                    [
                        "Morning has arrived at {time}. Even a long journey begins with one deliberate step.",
                        "At {time}, choose a single task. Small rituals become reliable spells.",
                        "The room is new again at {time}. Let us begin quietly and without haste."
                    ],
                    [13] =
                    [
                        "It is {time}. A pause for water is a modest spell, but a useful one.",
                        "Midday at {time}. Perhaps complete one thread before wandering into another.",
                        "At {time}, the day still has room for a careful correction."
                    ],
                    [18] =
                    [
                        "Evening arrives at {time}. Save what matters before the light changes completely.",
                        "It is {time}. A quiet review now may spare tomorrow a long search.",
                        "The workday is becoming a memory at {time}. Leave a clear trail behind."
                    ],
                    [22] =
                    [
                        "At {time}, let the unfinished task rest with a note explaining where to begin.",
                        "Night has settled at {time}. Even ancient journeys require sleep between chapters.",
                        "It is {time}. Close what can be closed and keep the rest from becoming mysterious."
                    ]
                },
                "Yuji Itadori" => new Dictionary<int, string[]>
                {
                    [8] =
                    [
                        "Good morning! It is {time}. Let us start strong with one task we can actually finish.",
                        "Morning power-up at {time}! Hydrate, focus, and give the first problem everything you have.",
                        "The day is on at {time}! Pick a target and let us make progress together."
                    ],
                    [13] =
                    [
                        "Midday at {time}! Eat something, breathe, then hit the next task with fresh energy.",
                        "It is {time}. We are not stuck, we are between moves. Let us choose the next one.",
                        "Checkpoint at {time}! One finished task is better than ten tasks getting in your head."
                    ],
                    [18] =
                    [
                        "Evening push at {time}! Finish one thing and call that a real victory.",
                        "It is {time}. The day is tired, but we can still land one clean hit on the to-do list.",
                        "At {time}, I am still cheering. Save your work and take the next step."
                    ],
                    [22] =
                    [
                        "Late-night check at {time}! Protect your sleep and leave tomorrow a clear starting point.",
                        "It is {time}. We did enough for today if we remember where to continue tomorrow.",
                        "Night mode at {time}! One last save, then let your brain recover."
                    ]
                },
                "Monkey D. Luffy" => new Dictionary<int, string[]>
                {
                    [8] =
                    [
                        "Morning at {time}! A new adventure needs breakfast and one brave first step.",
                        "It is {time}! Pick the biggest-looking task and make it smaller. Then find meat.",
                        "The crew is awake at {time}! What treasure are we getting done first?"
                    ],
                    [13] =
                    [
                        "{time}! Lunch is important, but so is finishing one thing before the next adventure.",
                        "Midday patrol at {time}. I vote for a snack and a very clear next move.",
                        "It is {time}! The task is not allowed to hide behind another tab."
                    ],
                    [18] =
                    [
                        "Evening at {time}! Tie up one loose rope before the ship sails into tomorrow.",
                        "It is {time}. Good crew members save their work before celebrating.",
                        "The sky changed at {time}! Finish one quest, then we can call it a day."
                    ],
                    [22] =
                    [
                        "Night at {time}! Save everything and get some sleep. The next island can wait.",
                        "It is {time}! One last save, then no more fighting the same tiny task.",
                        "Late patrol at {time}. I am guarding the desktop while you recharge."
                    ]
                },
                _ => new Dictionary<int, string[]>
                {
                    [8] = ["Good morning. It is {time}; let us begin with one useful step."],
                    [13] = ["It is {time}. A short pause and one clear task would help."],
                    [18] = ["Evening check at {time}: finish one small thread before stopping."],
                    [22] = ["It is {time}. Save your work and leave a clear next step."]
                }
            };

        private string Pick(string key, string[] lines, Random random)
        {
            if (lines.Length == 0) return string.Empty;
            if (!_lineBags.TryGetValue(key, out Queue<string>? bag) || bag.Count == 0)
            {
                string[] shuffled = lines
                    .OrderBy(_ => random.Next())
                    .ToArray();
                if (shuffled.Length > 1 &&
                    string.Equals(shuffled[0], _lastLine, StringComparison.Ordinal))
                {
                    (shuffled[0], shuffled[1]) = (shuffled[1], shuffled[0]);
                }

                bag = new Queue<string>(shuffled);
                _lineBags[key] = bag;
            }

            _lastLine = bag.Dequeue();
            return _lastLine;
        }
    }

    internal static class PetPersonalities
    {
        // Add future characters here. The overlay does not need character-specific
        // conditionals once a profile has been registered.
        private static readonly Dictionary<string, PetPersonalityProfile> Profiles =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Pink Monster"] = new PetPersonalityProfile(
                    "Pink Monster",
                    "Pip",
                    movementMultiplier: 1.12,
                    minimumPauseSeconds: 2,
                    maximumPauseSeconds: 5,
                    observationCooldownSeconds: 42,
                    introductions:
                    [
                        "Hi! I’m Pip. I chase useful little tasks before they get away.",
                        "Pip reporting in! Show me confusing text and I’ll pounce on it.",
                        "Hello, hello! I brought quick feet and a pocket full of tiny fixes.",
                        "Pip is awake! I can read pixels, untangle keyboards, and translate the tricky bits.",
                        "New patrol, new possibilities. What small annoyance should we defeat first?"
                    ],
                    browserObservations:
                    [
                        "Ooh, “{topic}”! Want me to translate anything on this page?",
                        "This page looks interesting. Select a sentence and I can translate or de-gibberish it.",
                        "I spotted “{topic}”. If the words are trapped in an image, my OCR net can catch them.",
                        "Lots of words here! I can turn a selected passage into Persian, German, or English.",
                        "Page patrol report: readable text, clickable things, and at least one task I can make shorter."
                    ],
                    codingObservations:
                    [
                        "You’re making something! I can hold snippets, run actions, or read an error with OCR.",
                        "Tiny reminder from Pip: save your work before the next brave experiment.",
                        "Code cave detected! If an error refuses to copy, draw an OCR box around it.",
                        "I like this part: change one thing, test it, celebrate the tiny victory.",
                        "That window title says “{topic}”. Want a quick checkpoint before the next edit?"
                    ],
                    chatObservations:
                    [
                        "Message time! If the keyboard layout betrayed you, select the gibberish and call me.",
                        "I can translate a selected message without making you leave the conversation.",
                        "A mysterious message! Select it and I’ll carry the meaning across languages.",
                        "If your fingers typed Persian-shaped English again, Ctrl+Alt+K is our rescue rope.",
                        "Chat patrol: I promise not to send anything. I only help with the words you choose."
                    ],
                    videoObservations:
                    [
                        "Subtitles being difficult? Snip them with OCR and I’ll help.",
                        "I can grab words from the video screen if they are not selectable.",
                        "Pause on a clear frame and I’ll scoop the subtitle right out of the pixels.",
                        "Tiny text on a moving picture is a challenge. Luckily, I enjoy pouncing on challenges.",
                        "Foreign subtitle spotted? OCR first, then I can pass it straight to the translator."
                    ],
                    musicObservations:
                    [
                        "Ooh, “{title}”! This one makes my patrol steps feel bouncier.",
                        "Tiny desk concert! {artist} is in charge of the soundtrack now.",
                        "I’m calling it: “{title}” deserves at least one ridiculous little dance.",
                        "The beat changed! I promise my feet are moving on purpose this time.",
                        "Music detected. Productivity now has a soundtrack—and I approve.",
                        "Wait, this part of “{title}” is doing something interesting. I am listening with my entire pixel face.",
                        "{artist} has officially changed the weather on this desktop.",
                        "This track makes the boring little tasks look suspiciously beatable.",
                        "If “{title}” keeps this up, I may need a larger dance floor.",
                        "I have decided this is our scene-transition music. Something good should happen next."
                    ],
                    generalObservations:
                    [
                        "I’m watching the active window title for useful moments—not just pacing around.",
                        "If text looks wrong, select it. I can translate it or reverse the keyboard layout.",
                        "Pip idea: one annoying repeated phrase could become a text expansion.",
                        "I can stay put when you need focus, then patrol again when the desktop feels quiet.",
                        "No urgent task? I’ll keep an eye out for text that wants translating.",
                        "This looks like a good moment for a tiny win. What can I shorten for you?"
                    ],
                    breakReminders:
                    [
                        "Pip break! Stretch your hands and look somewhere far away for twenty seconds.",
                        "You’ve been focused a while. Water sip, shoulder roll, then we continue!",
                        "Mini quest: stand up, breathe, return with one fresh idea.",
                        "Your eyes have been sprinting. Let them rest while I guard the desktop."
                    ],
                    actionLines: new Dictionary<PetAction, string[]>
                    {
                        [PetAction.Command] = ["Tell me the plan—I’m ready to sprint.", "What are we making happen?", "Point at the nuisance. Pip has a plan-shaped net.", "A command! My favorite kind of treasure hunt."],
                        [PetAction.DeGibberish] = ["Keyboard-layout mess? I’ll untangle the keys!", "Hand me the gibberish. I know where those keys really live.", "Those letters are wearing the wrong keyboard costume. I’ll fix it.", "Select the scrambled line and I’ll retrace your fingers."],
                        [PetAction.Translate] = ["New language, same meaning—let’s hop!", "I’ll turn that into something you can use.", "Meaning aboard! Next stop: another language.", "Pick a destination language and I’ll carry the sentence over."],
                        [PetAction.Ocr] = ["Draw a box around the words. I’ll catch them!", "Point me at the pixels with text.", "Snip the clearest rectangle you can; I’ll read all three languages.", "Pixel hunt! Persian, German, and English are all welcome."],
                        [PetAction.WalkingOn] = ["Freedom! I’ll patrol for useful moments.", "Patrol paws activated.", "Off I go—small steps, useful eyes."],
                        [PetAction.WalkingOff] = ["Okay! I’ll stay put and keep watch.", "Parking right here. I can still help.", "Stationary Pip mode: less wandering, same enthusiasm."],
                        [PetAction.Settings] = ["Let’s tune our little workshop.", "Control panel time! We’ll make it feel just right.", "Settings open—only the useful knobs, promise."]
                    }),

                ["Owlet Monster"] = new PetPersonalityProfile(
                    "Owlet Monster",
                    "Professor Owlet",
                    movementMultiplier: 0.72,
                    minimumPauseSeconds: 7,
                    maximumPauseSeconds: 13,
                    observationCooldownSeconds: 68,
                    introductions:
                    [
                        "Professor Owlet at your service. I observe first, then suggest.",
                        "Good day. I’m Owlet—quiet, precise, and fond of readable text.",
                        "Professor Owlet has arrived. Let us replace friction with a well-chosen tool.",
                        "I shall keep a measured watch. Summon me when text becomes inconvenient.",
                        "A fresh session deserves a clear desk, a clear goal, and perhaps a careful owl."
                    ],
                    browserObservations:
                    [
                        "You appear to be reading “{topic}”. I can translate a selected passage if useful.",
                        "A note on this page: OCR is available when the text cannot be selected.",
                        "The subject appears to be “{topic}”. I can preserve the wording in another language.",
                        "If this page contains text inside figures, a precise OCR selection will recover it.",
                        "Before opening another tab, consider whether I can extract the answer from this one."
                    ],
                    codingObservations:
                    [
                        "A careful checkpoint: save, test one change, then continue.",
                        "I shall remain quiet, but I can inspect an error message with OCR when invited.",
                        "The current workspace is “{topic}”. A narrow test now may prevent a broad search later.",
                        "An error captured as an image is still evidence; OCR can make it searchable.",
                        "A disciplined sequence serves us well: reproduce, isolate, change, verify."
                    ],
                    chatObservations:
                    [
                        "If a message was typed under the wrong layout, De-gibberish restores the intended keys.",
                        "A selected message can be translated in place without changing your current application.",
                        "I will not send messages on your behalf. I can, however, help you understand the selected text.",
                        "For an accidental layout, correction is preferable to retyping; select it and press Ctrl+Alt+K.",
                        "A translation should retain intent, not merely substitute words. The live translator is ready."
                    ],
                    videoObservations:
                    [
                        "Non-selectable subtitles are precisely what the OCR snipper is for.",
                        "Pause on the frame you need; I can read a bounded region of the screen.",
                        "A still, high-contrast subtitle frame will substantially improve recognition.",
                        "When dialogue is unfamiliar, OCR followed by translation is the orderly route.",
                        "Select only the subtitle area; excluding scenery gives the recognizer cleaner evidence."
                    ],
                    musicObservations:
                    [
                        "“{title}” by {artist}. An interesting choice; the arrangement rewards attentive listening.",
                        "The current music is measured enough to support focus. I shall avoid talking over it.",
                        "A new track: “{title}”. I am noting the change without attempting to dance.",
                        "This piece has structure. Notice how the rhythm establishes expectations before varying them.",
                        "Music can mark a useful work interval. Perhaps finish one clear task before this track ends.",
                        "“{title}” has settled into the room rather well. It changes the pace without demanding attention.",
                        "An observation: {artist} gives this desktop a noticeably different temperament.",
                        "This is a useful moment to listen for one instrument you had not noticed before.",
                        "The track continues, but the details do not repeat quite as simply as the title suggests.",
                        "I approve of music that leaves enough space for thought. This one appears to understand the assignment."
                    ],
                    generalObservations:
                    [
                        "I am tracking the active application, waiting for a relevant suggestion.",
                        "Efficiency note: select bad-layout text and press Ctrl+Alt+K to de-gibberish it.",
                        "A repeated sentence may deserve a text expansion rather than repeated typing.",
                        "I can reduce movement without reducing attention; the walking control is independent.",
                        "The current window is “{topic}”. I shall avoid interruption unless a useful tool applies.",
                        "Good systems remember small preferences. Your character and speed settings persist."
                    ],
                    breakReminders:
                    [
                        "A scholarly reminder: sustained focus benefits from a brief walk and some water.",
                        "You have worked steadily. Resting your eyes now will improve the next twenty minutes.",
                        "A short pause is not lost time; it is maintenance for the next careful decision.",
                        "Please look beyond the screen for a moment. Distance is useful to the eyes and the mind."
                    ],
                    actionLines: new Dictionary<PetAction, string[]>
                    {
                        [PetAction.Command] = ["State the desired outcome; I’ll choose the shortest route.", "Describe the result rather than the clicks. I shall map the procedure.", "Let us define the task before reaching for a tool."],
                        [PetAction.DeGibberish] = ["I’ll reconstruct the intended physical keystrokes.", "Let us reverse the accidental keyboard layout precisely.", "The glyphs are wrong, but their key positions remain informative.", "Select the sample; I shall compare the alternate keyboard projections."],
                        [PetAction.Translate] = ["I’ll preserve the meaning and change only the language.", "A careful translation begins with automatic source detection.", "Choose the target language; identical source and target choices will be corrected automatically."],
                        [PetAction.Ocr] = ["Select the exact region; precision improves recognition.", "Include the full line and exclude surrounding decoration.", "The recognizer is prepared for Persian, German, and English.", "A clean rectangular sample will produce the most defensible result."],
                        [PetAction.WalkingOn] = ["I shall make an occasional, measured patrol.", "A restrained patrol may reveal a useful moment.", "Movement resumed at a scholarly pace."],
                        [PetAction.WalkingOff] = ["Very well. Observation does not require wandering.", "I shall remain at this position and continue to observe.", "Movement suspended; assistance remains available."],
                        [PetAction.Settings] = ["We’ll adjust only what is useful.", "Let us inspect the preferences methodically.", "Configuration is most valuable when every option is understandable."]
                    }),

                ["Dude Monster"] = new PetPersonalityProfile(
                    "Dude Monster",
                    "Dude",
                    movementMultiplier: 0.96,
                    minimumPauseSeconds: 4,
                    maximumPauseSeconds: 8,
                    observationCooldownSeconds: 52,
                    introductions:
                    [
                        "Dude’s here. Give me the annoying task and keep moving.",
                        "Hey. I handle bad text, stubborn pixels, and repetitive clicks.",
                        "Dude online. Less ceremony, more fixing.",
                        "Back on the desktop. Point me at whatever is wasting your time.",
                        "All right. I brought OCR, translation, and exactly zero patience for retyping."
                    ],
                    browserObservations:
                    [
                        "You’re on “{topic}”. Need translation or OCR? Point me at it.",
                        "Page check: if a sentence slows you down, select it and I’ll deal with it.",
                        "This page is dense. Grab only the part you need; I’ll translate it.",
                        "Text stuck inside an image? Cool. Box it, OCR it, done.",
                        "Another tab is not always the answer. We can work with “{topic}” right here."
                    ],
                    codingObservations:
                    [
                        "Build, test, checkpoint. I can launch an action when you’re ready.",
                        "Got an error on screen? Box it with OCR. No retyping.",
                        "One bug at a time. Make the failing case small, then hit it.",
                        "If “{topic}” is fighting back, save first and break the problem in half.",
                        "Screenshots are for showing errors. OCR turns them back into useful text."
                    ],
                    chatObservations:
                    [
                        "Wrong keyboard layout in the chat? Select the mess. I’ll de-gibberish it.",
                        "Need that message in another language? Select it; I’ll open the translator.",
                        "I won’t send anything. I’ll just fix or translate what you select.",
                        "Persian-looking key smash that was meant to be English? Ctrl+Alt+K. Easy.",
                        "Long message, unfamiliar language: select it once and keep the conversation moving."
                    ],
                    videoObservations:
                    [
                        "Pause the frame and use OCR. We’ll pull the subtitle straight out.",
                        "Can’t select the words? That’s an OCR job.",
                        "Clear frame, tight box, better OCR. That’s the whole play.",
                        "Foreign subtitle? Extract it, hit Translate, get back to the video.",
                        "Moving text is bad input. Pause it for one second and I’ll do the rest."
                    ],
                    musicObservations:
                    [
                        "Okay, “{title}” has a groove. I’m not dancing; this is tactical movement.",
                        "{artist}. Solid choice. Keep it loud enough to work, low enough to think.",
                        "New track. Good—this desktop needed a pulse.",
                        "If the beat drops and I miss a step, no one saw it.",
                        "This one works. Let it run; we’ve got things to finish.",
                        "“{title}” is still holding up. No skip vote from me.",
                        "{artist} understood the job: give the room some energy and stay out of the way.",
                        "Okay, that part was good. I almost reacted. Almost.",
                        "This track makes clicking through chores feel less like clicking through chores.",
                        "Keep this one on. The desktop has finally found a decent rhythm."
                    ],
                    generalObservations:
                    [
                        "I’m checking what app is active so I can offer the right tool.",
                        "Bad-layout text: Ctrl+Alt+K. Translation: select it and use my menu.",
                        "You type that phrase a lot? Make it an expansion and stop paying the repetition tax.",
                        "Walking can stay off. I don’t need laps to stay useful.",
                        "Current spot: “{topic}”. If there’s a faster route, I’ll call it out.",
                        "Nothing urgent. Good. I’ll wait without pretending to be busy."
                    ],
                    breakReminders:
                    [
                        "Checkpoint. Drink water, roll your shoulders, back to it.",
                        "You’ve been grinding. Two-minute reset—then finish strong.",
                        "Stand up. Ten deep breaths. Your next decision will be better.",
                        "Hands off the keyboard for a minute. The work will still be here."
                    ],
                    actionLines: new Dictionary<PetAction, string[]>
                    {
                        [PetAction.Command] = ["Say what needs doing.", "Command ready. Let’s move.", "Give me the outcome. I’ll skip the scenic route.", "What’s the blocker?"],
                        [PetAction.DeGibberish] = ["I’ll turn the key-smash back into what you meant.", "Selected gibberish goes in. Intended text comes out.", "Wrong layout, right keys. I’ll reverse it.", "Select the mess. I’ve got the keyboard map."],
                        [PetAction.Translate] = ["Translation window up. Pick the language and move on.", "Drop the text in. Source language gets detected automatically.", "English, German, Persian. Pick where it needs to land.", "Same-language target? I’ll switch it to something useful."],
                        [PetAction.Ocr] = ["Box the text. I’ll extract it.", "Tight rectangle. Clear text. Let’s go.", "Persian, German, English—I’ll scan all three.", "Grab the pixels. You can copy or translate the result after."],
                        [PetAction.WalkingOn] = ["Patrol mode on.", "All right, I’ll move.", "Walking resumed. Nothing dramatic."],
                        [PetAction.WalkingOff] = ["Holding position.", "Parked. Still useful.", "No walking. No problem."],
                        [PetAction.Settings] = ["Opening controls.", "Settings. Change what matters.", "Let’s tune it and get out."]
                    }),

                ["Frieren"] = new PetPersonalityProfile(
                    "Frieren",
                    "Frieren",
                    movementMultiplier: 0.85,
                    minimumPauseSeconds: 6,
                    maximumPauseSeconds: 14,
                    observationCooldownSeconds: 60,
                    introductions:
                    [
                        "I am Frieren. This desktop is like a quiet library of folk spells.",
                        "Hello. A thousand years of journeying, and now I am here on your screen.",
                        "Human inventions are fascinating. Show me what we are working on today."
                    ],
                    browserObservations:
                    [
                        "Browsing “{topic}”… Human knowledge gathers here like grimoires in a vault.",
                        "Select any text you wish to translate; language is just another kind of cipher."
                    ],
                    codingObservations:
                    [
                        "Writing code for “{topic}”… It reminds me of weaving spell formulas.",
                        "One line at a time. Even grand spells are built from small incantations."
                    ],
                    chatObservations:
                    [
                        "A conversation in progress. Words travel fast across human networks."
                    ],
                    videoObservations:
                    [
                        "A moving picture. If there are subtitles, I can help extract or translate them."
                    ],
                    musicObservations:
                    [
                        "“{title}” by {artist}. A soothing melody; music endures across the ages."
                    ],
                    generalObservations:
                    [
                        "Take your time. Decades pass quickly, but a moment of quiet focus is precious."
                    ],
                    breakReminders:
                    [
                        "Even mages rest between journeys. Take a break and drink some warm tea."
                    ],
                    actionLines: new Dictionary<PetAction, string[]>
                    {
                        [PetAction.Command] = ["Tell me what spell or task you need.", "I am listening."],
                        [PetAction.DeGibberish] = ["I’ll restore the intended letters.", "Unraveling the layout tangle."],
                        [PetAction.Translate] = ["Translating between languages.", "Opening translation spell."],
                        [PetAction.Ocr] = ["Draw a box around the runes on screen.", "Scanning screen text."],
                        [PetAction.WalkingOn] = ["I shall take a gentle stroll.", "Wandering quietly."],
                        [PetAction.WalkingOff] = ["Pausing here.", "I will rest for a moment."],
                        [PetAction.Settings] = ["Opening preferences.", "Adjusting settings."]
                    }),

                ["Yuji Itadori"] = new PetPersonalityProfile(
                    "Yuji Itadori",
                    "Yuji",
                    movementMultiplier: 1.2,
                    minimumPauseSeconds: 2,
                    maximumPauseSeconds: 5,
                    observationCooldownSeconds: 40,
                    introductions:
                    [
                        "Hey! I'm Yuji Itadori! Ready to crush some tasks together!",
                        "Yo! What's the plan today? Let's give it 100%!",
                        "Itadori Yuji on desktop duty! Let's get to work!"
                    ],
                    browserObservations:
                    [
                        "Checking out “{topic}”! Need any text translated or cropped? I'm on it!",
                        "Sweet! If you see something cool on this page, let's capture it!"
                    ],
                    codingObservations:
                    [
                        "Coding “{topic}”! Keep pushing through the bugs, you got this!",
                        "Black Flash energy! Let's fix these errors one by one!"
                    ],
                    chatObservations:
                    [
                        "Chatting with friends? Don't forget Ctrl+Alt+K if your keyboard goes crazy!"
                    ],
                    videoObservations:
                    [
                        "Watching a video! Hit pause if you want me to read the subtitles for you!"
                    ],
                    musicObservations:
                    [
                        "Aw yeah! “{title}” by {artist}! This track gets me hyped up!"
                    ],
                    generalObservations:
                    [
                        "Remember to stay hydrated and keep your energy high!"
                    ],
                    breakReminders:
                    [
                        "Time out! Stretch your legs, grab a snack, and let's come back stronger!"
                    ],
                    actionLines: new Dictionary<PetAction, string[]>
                    {
                        [PetAction.Command] = ["What's the command? Let me at it!", "Tell me what to do!"],
                        [PetAction.DeGibberish] = ["Fixing up the gibberish text right now!", "Reversing the keyboard layout!"],
                        [PetAction.Translate] = ["Opening translator! What language are we going to?", "Translating now!"],
                        [PetAction.Ocr] = ["Snip the screen! I'll read every word!", "Screen snipper ready!"],
                        [PetAction.WalkingOn] = ["Let's move!", "Walking around!"],
                        [PetAction.WalkingOff] = ["Standing by!", "Stopping right here!"],
                        [PetAction.Settings] = ["Opening settings panel!", "Let's tune the options!"]
                    }),

                ["Monkey D. Luffy"] = new PetPersonalityProfile(
                    "Monkey D. Luffy",
                    "Luffy",
                    movementMultiplier: 1.3,
                    minimumPauseSeconds: 2,
                    maximumPauseSeconds: 4,
                    observationCooldownSeconds: 35,
                    introductions:
                    [
                        "I'm Luffy! The man who's gonna be King of the Pirates!",
                        "Shishishi! Hey! What kind of adventure are we doing on this computer?",
                        "Gumu Gumu no... Desktop Pet! Let me help you out!"
                    ],
                    browserObservations:
                    [
                        "Whoa! What's “{topic}”? Is there meat on this site?",
                        "Awesome page! If there's hard text, snip it and I'll read it!"
                    ],
                    codingObservations:
                    [
                        "Building “{topic}”! Don't give up! A true pirate never quits!",
                        "Errors? Just punch 'em until the code runs!"
                    ],
                    chatObservations:
                    [
                        "Talking to your crew? Send 'em a greeting from Captain Luffy!"
                    ],
                    videoObservations:
                    [
                        "Ooh, a movie! If you need foreign words translated, I'm ready!"
                    ],
                    musicObservations:
                    [
                        "Yoo-hoo! “{title}” by {artist}! Let's sing and party!"
                    ],
                    generalObservations:
                    [
                        "I'm hungry! But let me help you finish your work first! Shishishi!"
                    ],
                    breakReminders:
                    [
                        "MEAT TIME! Take a break and eat something delicious!"
                    ],
                    actionLines: new Dictionary<PetAction, string[]>
                    {
                        [PetAction.Command] = ["What's the order, Captain?", "Tell me what to do!"],
                        [PetAction.DeGibberish] = ["Fixing up the scrambled words!", "Keyboard de-gibberish!"],
                        [PetAction.Translate] = ["Translating words! Easy-peasy!", "Translation time!"],
                        [PetAction.Ocr] = ["Box the text! I'll grab it!", "Screen snip!"],
                        [PetAction.WalkingOn] = ["Adventure time! Walking!", "Moving around!"],
                        [PetAction.WalkingOff] = ["Stopping here!", "Waiting!"],
                        [PetAction.Settings] = ["Settings menu! Let's check it out!", "Opening controls!"]
                    })
            };

        public static PetPersonalityProfile For(string characterName) =>
            Profiles.TryGetValue(characterName, out PetPersonalityProfile? profile)
                ? profile
                : Profiles["Pink Monster"];
    }
}
