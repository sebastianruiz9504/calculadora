using System.Reflection;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Models.SoporteCloud;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.SoporteCloud;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class SoporteCloudSharedStateControllerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "soporte-cloud-controller-" + Guid.NewGuid().ToString("N"));
    private const string Code = "SYNCSAMPLE";
    private static readonly string SessionId = Guid.NewGuid().ToString("D");
    private const string QuestionId = "11111111-1111-1111-1111-111111111111";
    private const string CorrectId = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task SeparateWorkersSharePhaseAnswerAndRecoveryWithoutDataversePolling()
    {
        var first = Store();
        var second = Store();
        await Seed(first);
        var admin = Controller(first);
        var participant = Controller(second);
        Assert.IsType<OkObjectResult>(await admin.AdvanceLiveSurvey(SessionId, default));
        Assert.Equal("intro", State(await participant.PublicLiveSurveyState(Code, "", default)).Phase);
        Assert.IsType<OkObjectResult>(await admin.AdvanceLiveSurvey(SessionId, default));
        Assert.Equal("question", State(await participant.PublicLiveSurveyState(Code, "", default)).Phase);
        Assert.IsType<OkObjectResult>(await participant.PublicLiveSurveyRegister(Code, Registration("participant"), default));
        var answer = State(await participant.PublicLiveSurveyAnswer(Code, new SoporteCloudLiveSurveyAnswerRequest
        {
            ParticipantKey = "participant@example.org", QuestionId = QuestionId, OptionId = CorrectId
        }, default));
        Assert.Equal(10m, answer.ParticipantProgress!.Score);
        var adminState = State(await admin.LiveSurveyState(SessionId, default));
        Assert.Equal(1, adminState.RegisteredCount);
        Assert.Equal(1, adminState.CurrentQuestionAnsweredCount);
        var restarted = Controller(Store());
        var recovered = State(await restarted.PublicLiveSurveyState(Code, "participant@example.org", default));
        Assert.Equal("question", recovered.Phase);
        Assert.Equal(10m, recovered.ParticipantProgress!.Score);
        Assert.Single(recovered.ParticipantProgress.Answers);
        Assert.Single((await Store().ReadAsync<LiveSurveySessionState>("code-" + Code))!.SatisfactionQuestions);
    }

    [Fact]
    public async Task RestoredStatePreservesCaseInsensitiveParticipantAndAnswerKeys()
    {
        var store = Store();
        await Seed(store);
        await store.MutateAsync<LiveSurveySessionState, bool>("code-" + Code, state =>
        {
            var participant = new LiveSurveyParticipantState { ParticipantKey = "Mixed@Example.org" };
            participant.Answers["ABCDEFAB-1111-1111-1111-111111111111"] = new LiveSurveyAnswerState();
            state!.Participants[participant.ParticipantKey] = participant;
            state.RemovedParticipants["Removed@Example.org"] = DateTimeOffset.UtcNow;
            return new(state, true);
        });
        var restored = (await Store().ReadAsync<LiveSurveySessionState>("code-" + Code))!;
        Assert.True(restored.Participants["mixed@example.org"].Answers.ContainsKey("abcdefab-1111-1111-1111-111111111111"));
        Assert.True(restored.RemovedParticipants.ContainsKey("removed@example.org"));
    }

    [Fact]
    public async Task ConcurrentReadersPersistExpiredQuestionTransitionOnlyOnce()
    {
        var first = Store();
        var second = Store();
        await Seed(first, expired: true);
        var results = await Task.WhenAll(
            Controller(first).PublicLiveSurveyState(Code, "", default),
            Controller(second).PublicLiveSurveyState(Code, "", default));
        Assert.All(results, result => { Assert.Equal("ranking", State(result).Phase); Assert.Equal(8, State(result).Sequence); });
        var persisted = await Store().ReadAsync<LiveSurveySessionState>("code-" + Code);
        Assert.Equal(8, persisted!.Sequence);
        Assert.Equal("ranking", persisted.Phase);
    }

    [Fact]
    public async Task ConcurrentRegistrationsDoNotOverwriteOtherWorkersParticipants()
    {
        var first = Store();
        var second = Store();
        await Seed(first);
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            Controller(index % 2 == 0 ? first : second).PublicLiveSurveyRegister(Code, Registration("person" + index), default)));
        Assert.All(results, result => Assert.IsType<OkObjectResult>(result));
        var persisted = await Store().ReadAsync<LiveSurveySessionState>("code-" + Code);
        Assert.Equal(20, persisted!.Participants.Count);
        Assert.Equal(20, persisted.Sequence);
    }

    [Fact]
    public async Task ClosingSessionRejectsNewAnswersWithoutMutatingSavedProgress()
    {
        var store = Store();
        await Seed(store);
        await store.MutateAsync<LiveSurveySessionState, bool>("code-" + Code, state =>
        {
            state!.ClosingOperationId = "in-progress";
            state.ClosingStartedUtc = DateTimeOffset.UtcNow;
            return new(state, true);
        });
        Assert.IsType<BadRequestObjectResult>(await Controller(Store()).PublicLiveSurveyRegister(Code, Registration("blocked"), default));
        Assert.Empty((await store.ReadAsync<LiveSurveySessionState>("code-" + Code))!.Participants);
    }

    [Fact]
    public async Task FailedCloseReleasesLeaseAndRetryPersistsTheSameAnswersBeforeClosing()
    {
        var store = Store();
        await Seed(store);
        var participantController = Controller(store);
        Assert.IsType<OkObjectResult>(await participantController.PublicLiveSurveyRegister(Code, Registration("closing"), default));
        await store.MutateAsync<LiveSurveySessionState, bool>("code-" + Code, state =>
        {
            var participant = state!.Participants["closing@example.org"];
            participant.Score = 10;
            participant.MaxScore = 10;
            participant.Answers[QuestionId] = new LiveSurveyAnswerState
            {
                QuestionId = QuestionId, OptionId = CorrectId, Points = 10, MaxPoints = 10, IsCorrect = true
            };
            return new(state, true);
        });
        var attempts = 0;
        IReadOnlyList<SoporteCloudSurveySubmitRequest>? saved = null;
        var dataverse = DispatchProxy.Create<IDataverseService, RejectDataverseProxy>();
        ((RejectDataverseProxy)(object)dataverse).Handler = (method, args) =>
        {
            if (method!.Name == nameof(IDataverseService.SaveSoporteCloudLiveKnowledgeResultsAsync))
            {
                attempts++;
                if (attempts == 1) return Task.FromException<int>(new InvalidOperationException("Simulated Dataverse failure"));
                saved = (IReadOnlyList<SoporteCloudSurveySubmitRequest>)args![1]!;
                return Task.FromResult(saved.Count);
            }
            if (method.Name == nameof(IDataverseService.CloseSoporteCloudSurveySessionAsync))
                return Task.FromResult(new SoporteCloudSurveySaveResultDto { Message = "closed" });
            throw new InvalidOperationException("Unexpected Dataverse request: " + method.Name);
        };
        var admin = Controller(Store(), dataverse);
        Assert.IsType<BadRequestObjectResult>(await admin.CloseLiveSurvey(SessionId, new() { DurationMinutes = 30 }, default));
        var afterFailure = await Store().ReadAsync<LiveSurveySessionState>("code-" + Code);
        Assert.Equal("", afterFailure!.ClosingOperationId);
        Assert.NotEqual("closed", afterFailure.Phase);
        Assert.Single(afterFailure.Participants["closing@example.org"].Answers);
        Assert.IsType<OkObjectResult>(await admin.CloseLiveSurvey(SessionId, new() { DurationMinutes = 30 }, default));
        var submission = Assert.Single(saved!);
        var answer = Assert.Single(submission.Answers);
        Assert.Equal(10m, answer.PointsOverride);
        Assert.Equal(10m, answer.MaxPointsOverride);
        Assert.True(answer.IsCorrectOverride);
        Assert.Equal(CorrectId, answer.OptionId);
        var afterSuccess = await Store().ReadAsync<LiveSurveySessionState>("code-" + Code);
        Assert.Equal("closed", afterSuccess!.Phase);
        Assert.Equal("", afterSuccess.ClosingOperationId);
        Assert.Single(afterSuccess.Participants["closing@example.org"].Answers);
    }

    private SharedLiveSurveyStore Store() => new(_directory, readCacheDuration: TimeSpan.Zero);
    private static SoporteCloudController Controller(SharedLiveSurveyStore store, IDataverseService? dataverse = null) => new(dataverse ?? DispatchProxy.Create<IDataverseService, RejectDataverseProxy>(), store)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };
    private static SoporteCloudLiveSurveyStateDto State(IActionResult result) => Assert.IsType<SoporteCloudLiveSurveyStateDto>(Assert.IsType<OkObjectResult>(result).Value);
    private static SoporteCloudLiveSurveyRegisterRequest Registration(string name) => new() { FullName = name, Email = name + "@example.org", Company = "Example" };
    private static async Task Seed(SharedLiveSurveyStore store, bool expired = false)
    {
        var state = new LiveSurveySessionState
        {
            SessionId = SessionId, Code = Code, SessionName = "Shared state test", TopicName = "Test",
            Phase = expired ? "question" : "registration", Sequence = expired ? 7 : 0,
            CurrentQuestionId = expired ? QuestionId : "", CurrentQuestionIndex = expired ? 0 : -1,
            QuestionStartedOnUtc = expired ? DateTimeOffset.UtcNow.AddSeconds(-21) : null,
            PendingPhase = "winners",
            KnowledgeQuestions = new[] { new SoporteCloudSurveyQuestionDto
            {
                QuestionId = QuestionId, ComponentValue = 645250000, InputTypeValue = 645250000,
                Text = "Question", IsActive = true,
                Options = new[] { new SoporteCloudSurveyOptionDto { OptionId = CorrectId, Text = "Correct", IsCorrect = true, IsActive = true } }
            } },
            SatisfactionQuestions = new[] { new SoporteCloudSurveyQuestionDto { QuestionId = "satisfaction", Text = "Rating", ComponentValue = 645250001, IsActive = true } }
        };
        await store.MutateAsync<LiveSurveySessionState, bool>("code-" + Code, _ => new(state, true));
        await store.MutateAsync<LiveSurveySessionIndex, bool>("session-" + SessionId, _ => new(new LiveSurveySessionIndex { Code = Code }, true));
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
    public class RejectDataverseProxy : DispatchProxy
    {
        public Func<MethodInfo?, object?[]?, object?>? Handler { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler is not null
            ? Handler(targetMethod, args)
            : throw new InvalidOperationException("Unexpected Dataverse request: " + targetMethod?.Name);
    }
}
