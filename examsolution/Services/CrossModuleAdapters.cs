using ExamAttemptService.Models;
using ExamAttemptService.Services;
using ExamService.Controllers.V1;
using ExamService.Models;
using ExamService.Services;
using MongoDB.Driver;
using NotificationService.Services;
using ProctoringService.Services;
using QuestionBankService.Services;
using ResultService.Models;
using ResultService.Services;

namespace examsolution.Services;

public sealed class ExamQuestionDetailResolver : IExamQuestionDetailResolver
{
    private readonly QuestionService _questionService;

    public ExamQuestionDetailResolver(QuestionService questionService)
    {
        _questionService = questionService;
    }

    public async Task<List<object>> ResolveQuestionsByIdsAsync(IReadOnlyList<string> questionIds)
    {
        var questions = await _questionService.GetQuestionsByIdsAsync(questionIds.ToList());
        return questions.Select(q => (object)QuestionService.ToQuestionWithAnswer(q)).ToList();
    }
}

public sealed class AttemptExamProviderAdapter : IAttemptExamProvider
{
    private readonly ExamManagementService _examService;
    private readonly QuestionService _questionService;

    public AttemptExamProviderAdapter(ExamManagementService examService, QuestionService questionService)
    {
        _examService = examService;
        _questionService = questionService;
    }

    public async Task<AttemptExamMetadata?> GetExamMetadataAsync(string examId)
    {
        var exam = await _examService.GetExamByIdAsync(examId);
        if (exam == null) return null;

        var questionIds = exam.QuestionIds
            .Concat(exam.Sections.OrderBy(s => s.OrderIndex).SelectMany(s => s.QuestionIds))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        return new AttemptExamMetadata
        {
            ExamId = exam.ExamId,
            DurationMinutes = exam.DurationMinutes,
            Status = exam.Status.ToString(),
            ScheduledStartTime = exam.ScheduledStartTime,
            ScheduledEndTime = exam.ScheduledEndTime,
            QuestionIds = questionIds,
            AllowedStudents = exam.AllowedStudents ?? new List<string>(),
            BlockedStudents = exam.BlockedStudents ?? new List<string>(),
            RandomizeQuestions = exam.Settings?.RandomizeQuestions ?? false
        };
    }

    public async Task<List<AttemptQuestionDto>> GetSanitizedQuestionsAsync(IReadOnlyList<string> questionIds)
    {
        var questions = await _questionService.GetQuestionsByIdsAsync(questionIds.ToList());
        return questions.Select(q => new AttemptQuestionDto
        {
            Id = q.Id,
            QuestionText = q.QuestionText,
            Type = q.Type.ToString(),
            Marks = q.Marks,
            NegativeMarks = q.NegativeMarks,
            ImageUrl = q.ImageUrl,
            CodeSnippet = q.CodeSnippet,
            Options = q.Options.Select(o => new AttemptQuestionOptionDto
            {
                Id = o.Id,
                Text = o.Text,
                ImageUrl = o.ImageUrl
            }).ToList()
        }).ToList();
    }
}

public sealed class AttemptEvaluationSourceAdapter : IAttemptEvaluationSource
{
    private readonly ExamAttemptService.Services.MongoDbService _attemptDb;
    private readonly ExamManagementService _examService;
    private readonly QuestionService _questionService;
    private readonly NotificationManagementService _notificationService;

    public AttemptEvaluationSourceAdapter(
        ExamAttemptService.Services.MongoDbService attemptDb,
        ExamManagementService examService,
        QuestionService questionService,
        NotificationManagementService notificationService)
    {
        _attemptDb = attemptDb;
        _examService = examService;
        _questionService = questionService;
        _notificationService = notificationService;
    }

    public async Task<EvaluationInputData?> LoadEvaluationDataAsync(string attemptId)
    {
        var attempt = await _attemptDb.ExamAttempts.Find(a => a.Id == attemptId).FirstOrDefaultAsync();
        if (attempt == null) return null;

        var exam = await _examService.GetExamByIdAsync(attempt.ExamId);
        var questionIds = new HashSet<string>(attempt.Answers.Select(a => a.QuestionId));
        if (exam != null)
        {
            foreach (var qId in exam.QuestionIds) questionIds.Add(qId);
            foreach (var sec in exam.Sections)
            foreach (var qId in sec.QuestionIds) questionIds.Add(qId);
        }

        var questions = await _questionService.GetQuestionsByIdsAsync(questionIds.ToList());
        var answerMap = attempt.Answers.ToDictionary(a => a.QuestionId);

        var input = new EvaluationInputData
        {
            AttemptId = attempt.Id,
            ExamId = attempt.ExamId,
            ExamTitle = exam?.Title ?? attempt.ExamId,
            UserId = attempt.StudentId,
            StudentName = attempt.StudentName,
            TotalMarks = exam?.TotalMarks ?? 0,
            PassingMarks = exam?.PassingMarks ?? 0,
            EnableNegativeMarking = exam?.Grading?.EnableNegativeMarking ?? false,
            NegativeMarkingPercentage = exam?.Grading?.NegativeMarkingPercentage ?? 0,
            ShowResultsImmediately = exam?.Settings?.ShowResultsImmediately ?? false,
            Questions = new List<EvaluationQuestionInput>()
        };

        foreach (var q in questions)
        {
            answerMap.TryGetValue(q.Id, out var ans);
            input.Questions.Add(new EvaluationQuestionInput
            {
                QuestionId = q.Id,
                QuestionType = q.Type.ToString(),
                Marks = q.Marks,
                NegativeMarks = q.NegativeMarks,
                CorrectAnswer = q.CorrectAnswer,
                CorrectOptions = q.CorrectOptions ?? new List<string>(),
                SelectedOption = ans?.SelectedOption,
                SelectedOptions = ans?.SelectedOptions ?? new List<string>(),
                TextAnswer = ans?.TextAnswer,
                CodeAnswer = ans?.CodeAnswer
            });
        }

        return input;
    }

    public async Task UpdateEvaluatedAttemptAsync(
        string attemptId,
        double score,
        double percentage,
        bool passed,
        IReadOnlyList<QuestionGrade> breakdown)
    {
        var attempt = await _attemptDb.ExamAttempts.Find(a => a.Id == attemptId).FirstOrDefaultAsync();
        if (attempt == null) return;

        var gradeMap = breakdown.ToDictionary(b => b.QuestionId);
        foreach (var ans in attempt.Answers)
        {
            if (gradeMap.TryGetValue(ans.QuestionId, out var g))
            {
                ans.IsCorrect = g.IsCorrect;
                ans.MarksAwarded = g.Score;
            }
        }

        var update = Builders<ExamAttempt>.Update
            .Set(a => a.Answers, attempt.Answers)
            .Set(a => a.Score, score)
            .Set(a => a.Percentage, percentage)
            .Set(a => a.Result, passed ? "Pass" : "Fail")
            .Set(a => a.Status, AttemptStatus.Evaluated);

        await _attemptDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);
    }

    public async Task NotifyResultPublishedAsync(string userId, string examId, double score, double maxScore, bool passed)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        var exam = await _examService.GetExamByIdAsync(examId);
        double pct = maxScore > 0 ? (score / maxScore) * 100.0 : 0;
        await _notificationService.SendResultPublishedAsync(userId, exam?.Title ?? examId, pct, passed ? "Pass" : "Fail");
    }
}

public sealed class AttemptSubmittedHandlerAdapter : IAttemptSubmittedHandler
{
    private readonly EvaluationService _evaluationService;

    public AttemptSubmittedHandlerAdapter(EvaluationService evaluationService)
    {
        _evaluationService = evaluationService;
    }

    public async Task OnAttemptSubmittedAsync(string attemptId)
    {
        await _evaluationService.EvaluateAttemptAsync(attemptId);
    }
}

public sealed class ProctoringAttemptSuspenderAdapter : IProctoringAttemptSuspender
{
    private readonly AttemptManagementService _attemptService;

    public ProctoringAttemptSuspenderAdapter(AttemptManagementService attemptService)
    {
        _attemptService = attemptService;
    }

    public Task SuspendAttemptAsync(string attemptId, string reason) =>
        _attemptService.TerminateAttemptAsync(attemptId, $"Proctor suspension: {reason}");
}

public sealed class ExamAnalyticsProviderAdapter : IExamAnalyticsProvider
{
    private readonly ExamAttemptService.Services.MongoDbService _attemptDb;
    private readonly ResultService.Services.MongoDbService _resultDb;

    public ExamAnalyticsProviderAdapter(
        ExamAttemptService.Services.MongoDbService attemptDb,
        ResultService.Services.MongoDbService resultDb)
    {
        _attemptDb = attemptDb;
        _resultDb = resultDb;
    }

    public async Task<ExamStatisticsDto> GetExamStatisticsAsync(Exam exam)
    {
        var attempts = await _attemptDb.ExamAttempts.Find(a => a.ExamId == exam.ExamId).ToListAsync();
        var results = await _resultDb.Results.Find(r => r.ExamId == exam.ExamId).ToListAsync();

        int completed = attempts.Count(a => a.Status is AttemptStatus.Submitted or AttemptStatus.Evaluated);
        return new ExamStatisticsDto
        {
            ExamId = exam.ExamId,
            TotalAttempts = attempts.Count,
            TotalCompleted = completed,
            AverageScore = results.Count > 0 ? Math.Round(results.Average(r => r.TotalScore), 2) : 0,
            HighestScore = results.Count > 0 ? Math.Round(results.Max(r => r.TotalScore), 2) : 0,
            LowestScore = results.Count > 0 ? Math.Round(results.Min(r => r.TotalScore), 2) : 0,
            PassRatePercentage = results.Count > 0 ? Math.Round(results.Count(r => r.IsPassed) * 100.0 / results.Count, 2) : 0
        };
    }

    public async Task<PerformanceMetricsDto> GetPerformanceMetricsAsync(Exam exam)
    {
        var attempts = await _attemptDb.ExamAttempts.Find(a => a.ExamId == exam.ExamId && (a.Status == AttemptStatus.Submitted || a.Status == AttemptStatus.Evaluated)).ToListAsync();
        var results = await _resultDb.Results.Find(r => r.ExamId == exam.ExamId).ToListAsync();

        var sectionScores = new Dictionary<string, double>();
        var breakdownByQ = results
            .SelectMany(r => r.Breakdown)
            .GroupBy(b => b.QuestionId)
            .ToDictionary(g => g.Key, g => g.Average(b => b.MaxScore > 0 ? (b.Score / b.MaxScore) * 100.0 : 0));

        foreach (var sec in exam.Sections)
        {
            var secPcts = sec.QuestionIds.Where(breakdownByQ.ContainsKey).Select(id => breakdownByQ[id]).ToList();
            sectionScores[sec.Title] = secPcts.Count > 0 ? Math.Round(secPcts.Average(), 2) : 0;
        }

        double avgMinutes = attempts.Count > 0 ? Math.Round(attempts.Average(a => a.TimeSpentMinutes), 2) : 0;
        var sortedScores = results.Select(r => r.TotalScore).OrderByDescending(s => s).ToList();
        int topThreshold = sortedScores.Count > 0 ? (int)Math.Round(sortedScores[Math.Max(0, (int)Math.Floor(sortedScores.Count * 0.1) - 1)]) : 0;

        return new PerformanceMetricsDto
        {
            AverageScoreBySection = sectionScores,
            AverageCompletionTimeMinutes = avgMinutes,
            TopPercentileThreshold = topThreshold
        };
    }

    public async Task<CompletionStatsDto> GetCompletionStatsAsync(Exam exam)
    {
        var attempts = await _attemptDb.ExamAttempts.Find(a => a.ExamId == exam.ExamId).ToListAsync();
        int total = attempts.Count;
        int completed = attempts.Count(a => a.Status is AttemptStatus.Submitted or AttemptStatus.Evaluated);
        int dropped = attempts.Count(a => a.Status is AttemptStatus.Expired or AttemptStatus.Terminated or AttemptStatus.Invalidated);
        double avgMins = attempts.Where(a => a.Status is AttemptStatus.Submitted or AttemptStatus.Evaluated).Select(a => (double)a.TimeSpentMinutes).DefaultIfEmpty(0).Average();

        int wholeMins = (int)avgMins;
        int remSecs = (int)Math.Round((avgMins - wholeMins) * 60);

        return new CompletionStatsDto
        {
            CompletionRatePercentage = total > 0 ? Math.Round(completed * 100.0 / total, 2) : 0,
            DropoutRatePercentage = total > 0 ? Math.Round(dropped * 100.0 / total, 2) : 0,
            AverageTimeToComplete = $"{wholeMins}m {remSecs}s"
        };
    }

    public async Task<QuestionAnalysisDto> GetQuestionAnalysisAsync(Exam exam)
    {
        var results = await _resultDb.Results.Find(r => r.ExamId == exam.ExamId).ToListAsync();
        var metrics = results
            .SelectMany(r => r.Breakdown)
            .GroupBy(b => b.QuestionId)
            .Select(g =>
            {
                int count = g.Count();
                int correct = g.Count(b => b.IsCorrect);
                return new QuestionMetricDto
                {
                    QuestionId = g.Key,
                    AttemptCount = count,
                    CorrectCount = correct,
                    SuccessRatePercentage = count > 0 ? Math.Round(correct * 100.0 / count, 2) : 0
                };
            })
            .ToList();

        return new QuestionAnalysisDto
        {
            ToughestQuestions = metrics.OrderBy(m => m.SuccessRatePercentage).Take(5).ToList(),
            EasiestQuestions = metrics.OrderByDescending(m => m.SuccessRatePercentage).Take(5).ToList()
        };
    }
}
