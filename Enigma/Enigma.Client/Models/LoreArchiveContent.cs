namespace Enigma.Client.Models;

public sealed class LoreSectorImageResponse
{
    public string Status { get; set; } = string.Empty;
    public List<LoreSectorImageRecord> Images { get; set; } = [];
}

public sealed class LoreSectorImageRecord
{
    public string MapName { get; set; } = string.Empty;
    public string MapImage { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public string TimeFoundedDisplay { get; set; } = string.Empty;
}

public static class LoreArchiveContent
{
    public const string FinalQuestionLine = "Are we exploring the anomalies… or are the anomalies exploring us?";
    public const string JoinProgramCallToAction = "Join the Enigma Exploration Program";

    public static IReadOnlyList<string> BootLines { get; } =
    [
        "ENIGMA RESEARCH ARCHIVE",
        "INITIALIZING TERMINAL SESSION",
        "VERIFYING PUBLIC ACCESS CREDENTIALS",
        "SYNCING FILE INDEX",
        "DECRYPTING NON-RESTRICTED RECORDS",
        "CHECKING SIGNAL INTEGRITY",
        "ACCESS GRANTED",
    ];

    public static IReadOnlyList<ArchiveDirectoryGroupDefinition> DirectoryGroups { get; } =
    [
        new()
        {
            Key = "discovery",
            Title = "Discovery Brief",
            Description = "First contact, signal loss, and the machines built to survive it.",
            FileIds = ["enigma-corporation", "exploration-problem", "e-units"],
        },
        new()
        {
            Key = "operations",
            Title = "Field Operations",
            Description = "Explorer deployment, collapse events, and the cost of withdrawal.",
            FileIds = ["explorers", "anomaly-collapse", "expedition-failure", "anomaly-awareness"],
        },
        new()
        {
            Key = "sectors",
            Title = "Sector Intelligence",
            Description = "What remains after successful closure and what it continues to hide.",
            FileIds = ["sectors", "sector-reactivation", "sector-images", "maze-nuggets"],
        },
        new()
        {
            Key = "directive",
            Title = "Governance & Unknowns",
            Description = "Network control, strategic influence, and the final unresolved record.",
            FileIds = ["governance", "unanswered-questions"],
        },
    ];

    public static IReadOnlyDictionary<string, string> LegacyGroupDefaults { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["explorers"] = "explorers",
            ["sectors"] = "sectors",
            ["directive"] = "governance",
        };

    public static IReadOnlyList<ArchiveFileDefinition> Files { get; } =
    [
        new()
        {
            Id = "enigma-corporation",
            Code = "EARD-001",
            Title = "Enigma Corporation",
            Subtitle = "World authority on anomaly study and containment.",
            Classification = "Classified File",
            Summary = "Recovered intake file describing Enigma's role in studying anomalies and the impossible maze structures beyond them.",
            Status = ArchiveFileStatus.Open,
            ThreatLevel = "Moderate",
            SignalState = "Signal stable",
            IsUnlocked = true,
            Categories = ["Discovery Brief", "Open"],
            SortOrder = 1,
            RelatedFiles = ["exploration-problem", "e-units"],
            RightPanelType = ArchiveRightPanelType.GeospatialWatch,
            VisualTheme = "geospatial-watch",
            LastUpdatedText = "Observer intake revision // Mar 09, 2026",
            PreviewText = "Anomalies do not lead elsewhere on Earth. They open into deliberate artificial mazes.",
            PreviewFacts = ["Artificial chamber networks", "Constructed mechanisms", "No known author"],
            PrimaryAccent = "#79d3eb",
            SecondaryAccent = "#1f3f55",
            Lead = "Enigma Corporation is the world's leading authority on the study and containment of Anomalies, unexplained spatial rifts that appear suddenly in remote and unpredictable locations across the planet.",
            Paragraphs =
            [
                "These anomalies defy known physics. When entered, they do not lead to another physical location.",
                "Instead, they open into vast artificial environments: massive maze-like structures composed of interconnected chambers.",
                "Each chamber contains mechanisms, puzzles, and strange devices that appear intentionally constructed.",
                "No natural geological or technological process can explain their existence. No known civilization has claimed responsibility. And yet the structures appear deliberately designed."
            ],
            Highlights =
            [
                "Designed to test intelligence",
                "Designed to test perception",
                "Designed to test adaptability",
            ],
            FragmentLabel = "Decrypt Annotation",
            FragmentContent = "Classification review notes describe the mazes as intention without authorship: every chamber appears built to be understood by something inside it.",
        },
        new()
        {
            Id = "exploration-problem",
            Code = "EARD-014",
            Title = "The Exploration Problem",
            Subtitle = "Why early expeditions failed before they could be documented.",
            Classification = "Anomaly Report",
            Summary = "A failure board covering interference, sealed entry points, and the documentation blackout inside live anomalies.",
            Status = ArchiveFileStatus.Open,
            ThreatLevel = "Critical",
            SignalState = "Telemetry unstable",
            IsUnlocked = true,
            Categories = ["Discovery Brief", "Open"],
            SortOrder = 2,
            RelatedFiles = ["enigma-corporation", "e-units"],
            RightPanelType = ArchiveRightPanelType.InterferenceMatrix,
            VisualTheme = "interference-matrix",
            LastUpdatedText = "Incident synthesis // Mar 09, 2026",
            PreviewText = "The anomaly field disrupts cameras, radios, microphones, and traditional sensors before meaningful capture begins.",
            PreviewFacts = ["Entrance seals", "Sensor field collapse", "Expeditions lost"],
            PrimaryAccent = "#ffab7b",
            SecondaryAccent = "#41252d",
            Lead = "Early attempts to explore the anomalies ended in failure.",
            Paragraphs =
            [
                "Every anomaly emits a powerful interference field that disrupts nearly all forms of technology, including cameras, microphones, radio communication, and traditional sensors.",
                "Inside the anomaly environment, electronic systems behave unpredictably and most data cannot be transmitted outside.",
                "Human explorers quickly discovered two critical problems: the entrance seals after entry, and no reliable documentation can be captured.",
                "Many expeditions were lost. Without a way to observe or record the interior environments, the anomalies remained impossible to study until Enigma developed a new approach."
            ],
            Highlights =
            [
                "Entrance seals after entry",
                "No reliable documentation can be captured",
            ],
            FragmentLabel = "Open Systems Memo",
            FragmentContent = "Remote sensor spool 07 ends with a repeating note: signal integrity collapsed at the exact moment the field team reported the walls had begun to move.",
        },
        new()
        {
            Id = "e-units",
            Code = "EARD-027",
            Title = "Enigma Exploration Units (E-Units)",
            Subtitle = "Radar-first machines built to survive anomaly interiors.",
            Classification = "E-Unit System Brief",
            Summary = "A systems brief covering the machines that made anomalies legible without sending humans inside first.",
            Status = ArchiveFileStatus.Open,
            ThreatLevel = "Moderate",
            SignalState = "Scan stable",
            IsUnlocked = true,
            Categories = ["Discovery Brief", "Open"],
            SortOrder = 3,
            RelatedFiles = ["exploration-problem", "explorers"],
            RightPanelType = ArchiveRightPanelType.RadarReconstruction,
            VisualTheme = "radar-reconstruction",
            LastUpdatedText = "Remote systems brief // Mar 09, 2026",
            PreviewText = "E-Units replaced cameras with radar reconstruction, giving operators a usable map where vision failed.",
            PreviewFacts = ["Reflection-only scanning", "Puzzle mechanism detection", "Remote control"],
            PrimaryAccent = "#74e2d0",
            SecondaryAccent = "#143847",
            Lead = "Instead of sending humans directly into anomalies, Enigma deploys remotely operated exploration drones known as Exploration Units, or E-Units.",
            Paragraphs =
            [
                "These machines are built specifically to survive inside anomaly environments.",
                "Each E-Unit is equipped with experimental radar-based spatial scanning technology, allowing it to reconstruct the surrounding environment without relying on conventional visual sensors.",
                "The radar system converts spatial reflections into simplified visual representations, allowing operators outside the anomaly to navigate the environment indirectly.",
                "The result is not true vision. It is a spatial interpretation of the maze. But it is enough to explore."
            ],
            Highlights =
            [
                "Map unknown chambers",
                "Detect structural geometry",
                "Identify puzzle mechanisms",
                "Navigate maze environments",
            ],
            FragmentLabel = "Read Internal Comment",
            FragmentContent = "The first successful E-Unit run transmitted no imagery at all, only geometry. Researchers still describe it as the day the maze became legible.",
        },
        new()
        {
            Id = "explorers",
            Code = "EARD-041",
            Title = "The Explorers",
            Subtitle = "Public operators transformed the anomaly program into a live network.",
            Classification = "Operator Dossier",
            Summary = "An observer dossier on why Enigma opened E-Unit control to the public and what explorers were asked to do.",
            Status = ArchiveFileStatus.Observed,
            ThreatLevel = "Elevated",
            SignalState = "Observer relay",
            Categories = ["Field Operations", "Observed"],
            SortOrder = 4,
            RelatedFiles = ["e-units", "anomaly-collapse", "governance"],
            RightPanelType = ArchiveRightPanelType.ExplorerRoster,
            VisualTheme = "explorer-roster",
            LastUpdatedText = "Observer file // Mar 09, 2026",
            PreviewText = "Explorers remotely guide E-Units, solve puzzle systems, and bring back the first reliable anomaly maps.",
            PreviewFacts = ["Public operators", "Verified anomaly maps", "Resource recovery"],
            PrimaryAccent = "#6ddfca",
            SecondaryAccent = "#163844",
            Lead = "Enigma eventually made a surprising decision: rather than limiting exploration to internal researchers, the corporation opened its anomaly program to the public.",
            Paragraphs =
            [
                "Participants known as Explorers remotely control E-Units and venture into anomalies across the world.",
                "When an E-Unit successfully escapes an anomaly, the collected radar data is compiled into a verified Anomaly Map.",
                "These maps represent the first reliable documentation of the internal structures.",
                "However, the most extraordinary discovery occurs only when an anomaly is fully solved."
            ],
            Highlights =
            [
                "Map unknown chambers",
                "Solve puzzle systems",
                "Collect anomaly resources",
                "Locate the exit of the maze",
            ],
            FragmentLabel = "Read Internal Comment",
            FragmentContent = "Opening access to the public solved a staffing problem and created something harder to measure: a global population of people willing to keep going when the maze stopped making sense.",
            UnlockRequirements =
            [
                new() { Type = ArchiveUnlockRuleType.ViewedFile, Value = "e-units" },
            ],
            UnlockedStatus = ArchiveFileStatus.Open,
        },
        new()
        {
            Id = "anomaly-collapse",
            Code = "EARD-058",
            Title = "Anomaly Collapse",
            Subtitle = "The procedural sequence that ends with a Sector.",
            Classification = "Collapse Event Log",
            Summary = "A locked collapse log covering what happens when an anomaly is fully solved instead of abandoned.",
            Status = ArchiveFileStatus.Locked,
            ThreatLevel = "Volatile",
            SignalState = "Containment watch",
            Categories = ["Field Operations", "Locked"],
            SortOrder = 5,
            RelatedFiles = ["explorers", "expedition-failure", "sectors"],
            RightPanelType = ArchiveRightPanelType.CollapseSequence,
            VisualTheme = "collapse-sequence",
            LastUpdatedText = "Closed file // Mar 09, 2026",
            PreviewText = "Completed anomalies do not simply vanish. They fold inward and leave a geometric object behind.",
            PreviewFacts = ["Puzzle completion", "Rift seals", "Sector remains"],
            PrimaryAccent = "#ffbc84",
            SecondaryAccent = "#3d2a2c",
            Lead = "When the final puzzle within an anomaly is completed, the environment begins to destabilize.",
            Paragraphs =
            [
                "The maze structure collapses inward.",
                "The spatial rift seals itself.",
                "And in the place where the anomaly once existed, a new object appears: a perfect geometric cube.",
                "Enigma researchers now refer to these objects as Sectors."
            ],
            Highlights =
            [
                "Maze structure folds inward",
                "Rift seals at the point of entry",
                "A perfect geometric cube remains",
            ],
            FragmentLabel = "Open Systems Memo",
            FragmentContent = "Sector formation is too clean to be debris. The collapse behaves like archival compression.",
            UnlockRequirements =
            [
                new() { Type = ArchiveUnlockRuleType.ViewedFile, Value = "explorers" },
            ],
        },
        new()
        {
            Id = "expedition-failure",
            Code = "EARD-066",
            Title = "Expedition Failure",
            Subtitle = "Abandonment does not create a Sector. It erases the anomaly.",
            Classification = "Loss Memorandum",
            Summary = "A corrupted loss memo covering what anomalies do when an explorer abandons an active run.",
            Status = ArchiveFileStatus.Corrupted,
            ThreatLevel = "Critical",
            SignalState = "Signal fractured",
            IsCorrupted = true,
            Categories = ["Field Operations", "Corrupted"],
            SortOrder = 6,
            RelatedFiles = ["anomaly-collapse", "anomaly-awareness"],
            RightPanelType = ArchiveRightPanelType.LossPlayback,
            VisualTheme = "loss-playback",
            LastUpdatedText = "Corrupted memorandum // Mar 09, 2026",
            PreviewText = "Abandoned anomalies collapse differently. They do not compress into Sectors. They disappear.",
            PreviewFacts = ["No sector forms", "No image survives", "Archive loss event"],
            PrimaryAccent = "#ff7d89",
            SecondaryAccent = "#40232c",
            Lead = "Researchers later discovered a second, far more troubling behavior.",
            Paragraphs =
            [
                "If an explorer abandons an expedition and deactivates their E-Unit inside an anomaly, the anomaly reacts immediately.",
                "Unlike a completed anomaly, which collapses into a Sector, abandoned anomalies behave differently.",
                "Instead of compressing into a cube, the anomaly simply vanishes. The spatial distortion collapses instantly, leaving no physical trace at the location where the anomaly once existed.",
                "No Sector forms. No spatial data is preserved. No Sector Image can be recovered. From Enigma's perspective, the anomaly is lost forever."
            ],
            Highlights =
            [
                "No Sector forms",
                "No spatial data is preserved",
                "No Sector Image can be recovered",
            ],
            FragmentLabel = "Open Systems Memo",
            FragmentContent = "The system records abandonment as disappearance, not failure. That wording was intentional.",
            UnlockRequirements =
            [
                new() { Type = ArchiveUnlockRuleType.ViewedFile, Value = "anomaly-collapse" },
            ],
            FragmentUnlockRequirements =
            [
                new() { Type = ArchiveUnlockRuleType.ViewedFile, Value = "anomaly-collapse" },
            ],
            UnlockedStatus = ArchiveFileStatus.Corrupted,
        },
        new()
        {
            Id = "anomaly-awareness",
            Code = "EARD-072",
            Title = "Anomaly Awareness",
            Subtitle = "Evidence suggests anomalies detect commitment and withdrawal.",
            Classification = "Behavioral Analysis",
            Summary = "A restricted analysis exploring whether the maze itself can detect abandonment and respond to intent.",
            Status = ArchiveFileStatus.Restricted,
            ThreatLevel = "Critical",
            SignalState = "Public redaction applied",
            IsRedacted = true,
            Categories = ["Field Operations", "Restricted"],
            SortOrder = 7,
            RelatedFiles = ["expedition-failure", "sectors", "unanswered-questions"],
            RightPanelType = ArchiveRightPanelType.BehaviorMonitor,
            VisualTheme = "behavior-monitor",
            LastUpdatedText = "Restricted analysis // Mar 09, 2026",
            PreviewText = "Anomalies may require an active participant to remain stable. Without one, they end themselves.",
            PreviewFacts = ["Commitment-bound ops", "Explorer presence required", "Self-terminating rift"],
            PrimaryAccent = "#ff9aa2",
            SecondaryAccent = "#2c3046",
            Lead = "This discovery led researchers to a disturbing hypothesis: anomalies appear capable of detecting when an explorer abandons the maze.",
            Paragraphs =
            [
                "The moment an E-Unit shuts down, the anomaly terminates the environment and closes the rift entirely.",
                "Because of this behavior, Enigma now classifies anomaly expeditions as commitment-bound operations.",
                "Once an explorer enters an anomaly, the only way to preserve the anomaly and its data is to reach the exit or complete the anomaly's final puzzle.",
                "This suggests the maze structures may require an active participant in order to remain stable. Without an explorer inside, the anomaly appears to have no reason to continue existing."
            ],
            Highlights =
            [
                "Reach the exit",
                "Or complete the anomaly's final puzzle",
            ],
            FragmentLabel = "Decrypt Annotation",
            FragmentContent = "If the anomaly can detect withdrawal, then participation may not be a side effect of the system. It may be the condition that powers it.",
            UnlockRequirements =
            [
                new() { Type = ArchiveUnlockRuleType.ViewedFile, Value = "expedition-failure" },
                new() { Type = ArchiveUnlockRuleType.ViewedFragment, Value = "expedition-failure" },
            ],
            UnlockedStatus = ArchiveFileStatus.Open,
        },
        new()
        {
            Id = "sectors",
            Code = "EARD-089",
            Title = "Sectors",
            Subtitle = "Stable cubes carrying compressed maze structure and discovery identity.",
            Classification = "Sector Analysis",
            Summary = "A locked sector-analysis record covering what Enigma found inside completed anomaly cubes.",
            Status = ArchiveFileStatus.Locked,
            ThreatLevel = "Moderate",
            SignalState = "Containment stable",
            Categories = ["Sector Intelligence", "Locked"],
            SortOrder = 8,
            RelatedFiles = ["anomaly-collapse", "sector-reactivation", "maze-nuggets"],
            RightPanelType = ArchiveRightPanelType.CubeAnalysis,
            VisualTheme = "cube-analysis",
            LastUpdatedText = "Closed cube log // Mar 09, 2026",
            PreviewText = "Each Sector contains compressed spatial data from the anomaly that produced it.",
            PreviewFacts = ["Maze geometry", "Puzzle mechanisms", "Primary discoverer binding"],
            PrimaryAccent = "#82c5ff",
            SecondaryAccent = "#173348",
            Lead = "Initial analysis revealed something extraordinary: each Sector contains compressed spatial data from the anomaly that produced it.",
            Paragraphs =
            [
                "Encoded within the cube are traces of maze geometry, puzzle mechanisms, environmental structure, and Maze Nugget distribution.",
                "It is as if the anomaly records its own structure before collapsing into a stable physical form.",
                "The explorer responsible for completing the anomaly is permanently registered as its Primary Discoverer.",
                "With the discoverer's authorization, Enigma researchers may extract data from the Sector for further study. But the cubes revealed an even more surprising property. They can be reactivated."
            ],
            Highlights =
            [
                "Maze geometry",
                "Puzzle mechanisms",
                "Environmental structure",
                "Maze Nugget distribution",
            ],
            FragmentLabel = "Open Systems Memo",
            FragmentContent = "The compression ratio is impossible. Sector interiors should not fit inside a physical object that can be carried by one person.",
            UnlockRequirements =
            [
                new() { Type = ArchiveUnlockRuleType.ViewedFile, Value = "anomaly-collapse" },
            ],
        },
        new()
        {
            Id = "sector-reactivation",
            Code = "EARD-097",
            Title = "Sector Reactivation",
            Subtitle = "Reconstructed mazes return with new answers and the same machinery.",
            Classification = "Reactivation Protocol",
            Summary = "A hidden protocol describing the controlled reactivation of encoded mazes inside Sector cubes.",
            Status = ArchiveFileStatus.Hidden,
            ThreatLevel = "Volatile",
            SignalState = "Resonance active",
            IsHidden = true,
            Categories = ["Sector Intelligence", "Hidden"],
            SortOrder = 9,
            RelatedFiles = ["sectors", "sector-images", "maze-nuggets"],
            RightPanelType = ArchiveRightPanelType.SpatialReconstruction,
            VisualTheme = "spatial-reconstruction",
            LastUpdatedText = "Recovered protocol // Mar 09, 2026",
            PreviewText = "Sectors can reconstruct the original maze, but the answers change every time they are accessed.",
            PreviewFacts = ["Same structure", "Different answers", "Unknown process remains active"],
            PrimaryAccent = "#92c3ff",
            SecondaryAccent = "#25385b",
            Lead = "Using specialized resonance and spatial reconstruction systems, Enigma scientists discovered a method for reactivating the internal environment encoded inside a Sector.",
            Paragraphs =
            [
                "When activated, the Sector temporarily reconstructs the maze that once existed inside the original anomaly.",
                "The structure, layout, and puzzle mechanisms remain identical. However, every access produces one strange phenomenon: the puzzle answers change.",
                "Solutions rearrange themselves instantly, generating new answer patterns while maintaining the same physical mechanisms.",
                "Sectors have since become essential tools for explorer training, puzzle research, Maze Nugget extraction, and anomaly simulation. However, nothing inside the cube appears capable of generating these new puzzle configurations. Which suggests something else may still be operating within the system. Something unseen."
            ],
            Highlights =
            [
                "Explorer training",
                "Puzzle research",
                "Maze Nugget extraction",
                "Anomaly simulation",
            ],
            FragmentLabel = "Read Internal Comment",
            FragmentContent = "Researchers can reconstruct the structure. They still cannot identify what rewrites the answers.",
            UnlockRequirements =
            [
                new() { Type = ArchiveUnlockRuleType.ViewedFile, Value = "sectors" },
            ],
        },
        new()
        {
            Id = "sector-images",
            Code = "EARD-103",
            Title = "Sector Images",
            Subtitle = "Recovered visual fragments from the far side of anomaly collapse.",
            Classification = "Recovered Image Cache",
            Summary = "A corrupted image cache covering the single encoded visual fragment each completed anomaly appears to preserve.",
            Status = ArchiveFileStatus.Corrupted,
            ThreatLevel = "Volatile",
            SignalState = "Cache degraded",
            IsCorrupted = true,
            Categories = ["Sector Intelligence", "Corrupted"],
            SortOrder = 10,
            RelatedFiles = ["sector-reactivation", "unanswered-questions"],
            RightPanelType = ArchiveRightPanelType.RecoveredImageCache,
            VisualTheme = "recovered-image-cache",
            LastUpdatedText = "Corrupted cache // Mar 09, 2026",
            PreviewText = "Sector Images never show the maze. They show someplace else.",
            PreviewFacts = ["Single image fragment", "Non-Earth environments", "Other-side theory"],
            PrimaryAccent = "#ffd37b",
            SecondaryAccent = "#4a3b28",
            Lead = "Every completed anomaly produces one additional discovery: embedded within each Sector is a single fragment of encoded visual data.",
            Paragraphs =
            [
                "Enigma researchers call these fragments Sector Images.",
                "Using spatial decoding technology, researchers can reconstruct the image stored within the cube. However, the result is never a photograph of the maze itself.",
                "Instead, the images depict environments that explorers have never seen. Some show landscapes that do not exist anywhere on Earth. Others depict structures that appear partially natural and partially engineered.",
                "This led researchers to a new theory: the anomaly may not simply create a maze. It may be connecting two locations, constructing a puzzle-filled environment between them, then recording a single visual imprint from the other side before collapsing. If this theory is correct, then every Sector Image represents a glimpse into a place humanity has never reached. Yet one question remains unanswered. If the anomaly connects two worlds, why do explorers always exit back into ours?"
            ],
            FragmentLabel = "Decode Image Record",
            FragmentContent = "Sector Images are treated as evidence, not souvenirs. The image is the only thing in the cube that feels like a memory.",
            UnlockRequirements =
            [
                new() { Type = ArchiveUnlockRuleType.ViewedFile, Value = "sector-reactivation" },
            ],
            UnlockedStatus = ArchiveFileStatus.Open,
        },
        new()
        {
            Id = "maze-nuggets",
            Code = "EARD-118",
            Title = "Maze Nuggets (MN)",
            Subtitle = "Recovered crystalline matter with impossible density and unstable behavior.",
            Classification = "Material Dossier",
            Summary = "An observed specimen file covering the strange crystalline resource recovered during anomaly runs.",
            Status = ArchiveFileStatus.Observed,
            ThreatLevel = "Moderate",
            SignalState = "Lab watch",
            Categories = ["Sector Intelligence", "Observed"],
            SortOrder = 11,
            RelatedFiles = ["sectors", "governance"],
            RightPanelType = ArchiveRightPanelType.MaterialAnalysis,
            VisualTheme = "material-analysis",
            LastUpdatedText = "Specimen tray // Mar 09, 2026",
            PreviewText = "MN behaves like a new form of matter with too much energy in too little mass.",
            PreviewFacts = ["High energy density", "Unstable atomic structure", "Anomaly-dependent behavior"],
            PrimaryAccent = "#8ed7ff",
            SecondaryAccent = "#173445",
            Lead = "While exploring anomalies, E-Units frequently recover strange crystalline objects scattered throughout the maze structures.",
            Paragraphs =
            [
                "These materials are known as Maze Nuggets, or MN.",
                "Preliminary analysis suggests they possess several unusual properties.",
                "Even tiny fragments of MN contain enormous energy potential.",
                "Enigma researchers believe Maze Nuggets may represent a completely new form of matter. Their true origin, and purpose, remains unknown."
            ],
            Highlights =
            [
                "Extremely high energy density",
                "Unstable atomic structure",
                "Behavior that changes inside anomalies",
            ],
            FragmentLabel = "Open Specimen Warning",
            FragmentContent = "MN is catalogued as currency in the field and as an unanswered material event in the lab.",
            UnlockRequirements =
            [
                new() { Type = ArchiveUnlockRuleType.ViewedFile, Value = "sectors" },
            ],
            UnlockedStatus = ArchiveFileStatus.Open,
        },
        new()
        {
            Id = "governance",
            Code = "EARD-131",
            Title = "Governance",
            Subtitle = "Explorer influence shapes what Enigma studies next.",
            Classification = "Network Directive",
            Summary = "A restricted directive on explorer influence, research voting, and the uneasy politics of the network.",
            Status = ArchiveFileStatus.Restricted,
            ThreatLevel = "Elevated",
            SignalState = "Public redaction applied",
            IsRedacted = true,
            Categories = ["Governance & Unknowns", "Restricted"],
            SortOrder = 12,
            RelatedFiles = ["explorers", "sectors", "unanswered-questions"],
            RightPanelType = ArchiveRightPanelType.GovernanceLattice,
            VisualTheme = "governance-lattice",
            LastUpdatedText = "Restricted network memo // Mar 09, 2026",
            PreviewText = "Explorers do not only map anomalies. They also shape research priorities, funding, and containment strategy.",
            PreviewFacts = ["Distributed research model", "Explorer voting power", "Transparency dispute"],
            PrimaryAccent = "#86d7ff",
            SecondaryAccent = "#203351",
            Lead = "Enigma operates under a distributed research model.",
            Paragraphs =
            [
                "Explorers who contribute Sectors and discoveries gain influence within the Enigma network.",
                "This allows them to vote on research priorities, anomaly expeditions, exploration funding, and containment strategies.",
                "Explorers may also submit discoveries for research, trade Sectors with other explorers, and stake Sectors for analysis and rewards.",
                "Through this system, the exploration program continues to grow through the contributions of its explorers. However, some participants suspect Enigma may already understand more about the anomalies than it publicly admits."
            ],
            Highlights =
            [
                "Research priorities",
                "Anomaly expeditions",
                "Exploration funding",
                "Containment strategies",
            ],
            FragmentLabel = "Read Internal Comment",
            FragmentContent = "The governance model creates ownership, investment, and consent. It also creates the appearance of transparency.",
            UnlockRequirements =
            [
                new() { Type = ArchiveUnlockRuleType.ViewedFile, Value = "explorers" },
                new() { Type = ArchiveUnlockRuleType.ViewedFile, Value = "sectors" },
            ],
            UnlockedStatus = ArchiveFileStatus.Open,
        },
        new()
        {
            Id = "unanswered-questions",
            Code = "EARD-144",
            Title = "The Unanswered Questions",
            Subtitle = "The final redaction still ends in an open investigation.",
            Classification = "Final Redaction",
            Summary = "The final record that names the unresolved mysteries and leaves the archive open instead of complete.",
            Status = ArchiveFileStatus.Hidden,
            ThreatLevel = "Critical",
            SignalState = "Terminal severity",
            IsHidden = true,
            Categories = ["Governance & Unknowns", "Hidden"],
            SortOrder = 13,
            RelatedFiles = ["anomaly-awareness", "sector-images", "governance"],
            RightPanelType = ArchiveRightPanelType.FinalRedaction,
            VisualTheme = "final-redaction",
            LastUpdatedText = "Final redaction // Mar 09, 2026",
            PreviewText = "Despite years of research, the most important mysteries remain unsolved.",
            PreviewFacts = ["Origin unknown", "Puzzle purpose unknown", "Collapse purpose unknown"],
            PrimaryAccent = "#ff8e98",
            SecondaryAccent = "#321f29",
            Lead = "Despite years of research, the most important mysteries remain unsolved.",
            Paragraphs =
            [
                "Who created the anomalies?",
                "Why are the mazes filled with puzzles?",
                "Why do anomalies contain Maze Nuggets?",
                "Why does every anomaly appear designed to be explored?",
                "Why do completed anomalies collapse into Sectors?",
                "Why do abandoned anomalies disappear completely?",
            ],
            FragmentLabel = "View Final Redaction",
            FragmentContent = "Enigma Anomaly Research Division. E.A.R.D. Question Everything.",
            UnlockRequirements =
            [
                new() { Type = ArchiveUnlockRuleType.ViewedFile, Value = "anomaly-awareness" },
                new() { Type = ArchiveUnlockRuleType.ViewedFile, Value = "governance" },
                new() { Type = ArchiveUnlockRuleType.ViewedFile, Value = "sector-images" },
            ],
        },
    ];

    private static readonly IReadOnlyDictionary<string, ArchiveFileDefinition> FilesById =
        Files.ToDictionary(file => file.Id, StringComparer.OrdinalIgnoreCase);

    public static ArchiveFileDefinition DefaultFile => Files[0];

    public static ArchiveFileDefinition? GetFileById(string? fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return null;
        }

        return FilesById.TryGetValue(fileId.Trim(), out var file)
            ? file
            : null;
    }

    public static string GetCanonicalFileHref(string fileId)
    {
        return $"/lore?file={Uri.EscapeDataString(fileId)}";
    }

    public static string GetLegacyAliasHref(string legacyGroup)
    {
        return $"/lore/{legacyGroup.Trim().ToLowerInvariant()}";
    }

    public static bool TryResolveLegacyGroupDefaultFileId(string? legacyGroup, out string fileId)
    {
        if (!string.IsNullOrWhiteSpace(legacyGroup) &&
            LegacyGroupDefaults.TryGetValue(legacyGroup.Trim(), out fileId!))
        {
            return true;
        }

        fileId = string.Empty;
        return false;
    }

    public static ArchiveDirectoryGroupDefinition? GetGroupForFile(string fileId)
    {
        return DirectoryGroups.FirstOrDefault(group =>
            group.FileIds.Any(candidate => string.Equals(candidate, fileId, StringComparison.OrdinalIgnoreCase)));
    }
}
