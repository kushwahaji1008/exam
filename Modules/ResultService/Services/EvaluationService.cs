using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using System.Text.Json;

namespace ResultService.Services
{
    public class EvaluationService
    {
        private readonly MongoDbService _mongoDb;
        private readonly ILogger<EvaluationService> _logger;

        public EvaluationService(MongoDbService mongoDb, ILogger<EvaluationService> logger)
        {
            _mongoDb = mongoDb;
            _logger = logger;
        }

        public async Task<EvaluationResult> EvaluateAttemptAsync(string attemptId)
        {
            // Get the attempt
            var attemptsCollection = _mongoDb.AttemptsDatabase.GetCollection<BsonDocument>("exam_attempts");
            var attempt = await attemptsCollection.Find(Builders<BsonDocument>.Filter.Eq("_id", attemptId)).FirstOrDefaultAsync();

            if (attempt == null)
            {
                throw new Exception("Attempt not found");
            }

            // Get exam details
            var examId = attempt["ExamId"].AsString;
            var examsCollection = _mongoDb.ExamsDatabase.GetCollection<BsonDocument>("exams");
            var exam = await examsCollection.Find(Builders<BsonDocument>.Filter.Eq("_id", examId)).FirstOrDefaultAsync();

            if (exam == null)
            {
                throw new Exception("Exam not found");
            }

            var questionIds = exam["QuestionIds"].AsBsonArray.Select(q => q.AsString).ToList();
            var answers = attempt["Answers"].AsBsonArray;

            // Get questions
            var questionsCollection = _mongoDb.QuestionsDatabase.GetCollection<BsonDocument>("questions");
            var questionsFilter = Builders<BsonDocument>.Filter.In("_id", questionIds);
            var questions = await questionsCollection.Find(questionsFilter).ToListAsync();
            var questionDict = questions.ToDictionary(q => q["_id"].AsString, q => q);

            double totalScore = 0;
            int totalQuestions = questionIds.Count;
            int correctAnswers = 0;

            // Evaluate each answer
            var evaluatedAnswers = new List<BsonDocument>();
            foreach (BsonDocument answer in answers)
            {
                var questionId = answer["QuestionId"].AsString;
                if (!questionDict.ContainsKey(questionId))
                    continue;

                var question = questionDict[questionId];
                var questionType = question["Type"].AsInt32;
                var marks = question["Marks"].ToDouble();
                var negativeMarks = question.Contains("NegativeMarks") && !question["NegativeMarks"].IsBsonNull 
                    ? question["NegativeMarks"].ToDouble() 
                    : 0;

                bool isCorrect = false;
                double marksAwarded = 0;

                // Type 0 = MCQ (single), Type 1 = Multiple Correct
                if (questionType == 0) // MCQ
                {
                    var correctOptions = question["CorrectOptions"].AsBsonArray;
                    if (correctOptions.Count > 0)
                    {
                        var correctOption = correctOptions[0].AsString;
                        var selectedOption = answer.Contains("SelectedOption") && !answer["SelectedOption"].IsBsonNull
                            ? answer["SelectedOption"].AsString
                            : null;

                        if (selectedOption == correctOption)
                        {
                            isCorrect = true;
                            marksAwarded = marks;
                            correctAnswers++;
                        }
                        else if (!string.IsNullOrEmpty(selectedOption))
                        {
                            marksAwarded = -negativeMarks;
                        }
                    }
                }
                else if (questionType == 1) // Multiple Correct
                {
                    var correctOptions = question["CorrectOptions"].AsBsonArray.Select(o => o.AsString).ToHashSet();
                    var selectedOptions = answer.Contains("SelectedOptions") && !answer["SelectedOptions"].IsBsonNull
                        ? answer["SelectedOptions"].AsBsonArray.Select(o => o.AsString).ToHashSet()
                        : new HashSet<string>();

                    if (correctOptions.SetEquals(selectedOptions))
                    {
                        isCorrect = true;
                        marksAwarded = marks;
                        correctAnswers++;
                    }
                    else if (selectedOptions.Count > 0)
                    {
                        marksAwarded = -negativeMarks;
                    }
                }
                // For subjective/code - manual evaluation needed

                totalScore += marksAwarded;

                // Update answer with evaluation
                answer["IsCorrect"] = isCorrect;
                answer["MarksAwarded"] = marksAwarded;
                evaluatedAnswers.Add(answer);
            }

            var totalMarks = exam["TotalMarks"].ToDouble();
            var passingMarks = exam["PassingMarks"].ToDouble();
            var percentage = totalMarks > 0 ? (totalScore / totalMarks) * 100 : 0;
            var result = totalScore >= passingMarks ? "Pass" : "Fail";

            // Update attempt with results
            var update = Builders<BsonDocument>.Update
                .Set("Answers", new BsonArray(evaluatedAnswers))
                .Set("Score", totalScore)
                .Set("Percentage", percentage)
                .Set("Result", result)
                .Set("Status", 3); // 3 = Evaluated

            await attemptsCollection.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", attemptId),
                update
            );

            return new EvaluationResult
            {
                AttemptId = attemptId,
                TotalScore = totalScore,
                TotalMarks = totalMarks,
                Percentage = percentage,
                Result = result,
                CorrectAnswers = correctAnswers,
                TotalQuestions = totalQuestions
            };
        }
    }

    public class EvaluationResult
    {
        public string AttemptId { get; set; } = string.Empty;
        public double TotalScore { get; set; }
        public double TotalMarks { get; set; }
        public double Percentage { get; set; }
        public string Result { get; set; } = string.Empty;
        public int CorrectAnswers { get; set; }
        public int TotalQuestions { get; set; }
    }
}