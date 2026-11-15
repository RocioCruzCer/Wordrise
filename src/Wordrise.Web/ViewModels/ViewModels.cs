using System.ComponentModel.DataAnnotations;
using Wordrise.Web.Domain;
using Wordrise.Web.Domain.Game;
using Wordrise.Web.Services;

namespace Wordrise.Web.ViewModels;

public sealed class LoginInput
{
    [Required(ErrorMessage = "Enter your email.")]
    [EmailAddress(ErrorMessage = "Enter a valid email.")]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your password.")]
    [DataType(DataType.Password)]
    [StringLength(128)]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}

public sealed class SignUpInput
{
    [Required(ErrorMessage = "Enter your name.")]
    [StringLength(60, ErrorMessage = "Use 60 characters or fewer.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your email.")]
    [EmailAddress(ErrorMessage = "Enter a valid email.")]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a password.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Use at least 8 characters.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose your level.")]
    [EnumDataType(typeof(CefrLevel), ErrorMessage = "Choose A2, B1 or B2.")]
    public CefrLevel? Level { get; set; } = CefrLevel.B1;
}

public sealed class AccountPageModel
{
    public string ActiveTab { get; set; } = "login";
    public LoginInput Login { get; set; } = new();
    public SignUpInput SignUp { get; set; } = new();
}

public sealed record DashboardModel(
    UserSummary User,
    RoundState? TodayRound,
    UserStats Stats,
    CampProgress Camp,
    IReadOnlyList<ReviewItem> ReviewToday,
    string Greeting,
    DateOnly TodayDate,
    GoalProgress Goal,
    int WeeklyXp);

public sealed record PlayHubModel(
    UserSummary User,
    RoundState? TodayRound,
    int ReviewReady,
    RoundState? TrainingInProgress,
    int TrainingFinishedToday,
    int PastDailyWords,
    GoalProgress Goal,
    int BestSprint);

public sealed record RoundPageModel(RoundState Round, bool AudioEnabled, int SecondsToNextWord, TrainingRequest? Next, bool SoundEnabled);

public sealed record SprintPageModel(SprintState Sprint, bool SoundEnabled);

public sealed record LeagueModel(
    UserSummary User,
    CefrLevel Level,
    LeagueBoard Board,
    IReadOnlyList<GroupSummary> Groups,
    int WeeklyXp);

public sealed record GroupPageModel(GroupSummary Group, LeagueBoard Board);

public sealed record ProgressModel(
    UserSummary User,
    UserStats Stats,
    CampProgress Camp,
    IReadOnlyList<DayAttempts> LastSevenDays,
    IReadOnlyList<NotebookItem> Notebook);

public sealed record NotebookPageModel(IReadOnlyList<NotebookItem> Items, int Page, bool HasNext);

public sealed class SettingsInput
{
    [Required]
    [EnumDataType(typeof(CefrLevel))]
    public CefrLevel? Level { get; set; }

    public bool PronunciationAudio { get; set; }

    [Range(1, 20)]
    public int DailyGoal { get; set; } = 3;

    public bool SoundEnabled { get; set; }

    public bool ShowInLeagues { get; set; }
}

public sealed record ProfileModel(
    UserSummary User,
    UserStats Stats,
    CampProgress Camp,
    SettingsInput Settings,
    bool Saved,
    IReadOnlyList<AchievementView> Achievements);

public sealed record GuessRequest(string? Guess);
