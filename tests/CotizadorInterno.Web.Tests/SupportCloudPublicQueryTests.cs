using CotizadorInterno.Web.Services;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class SupportCloudPublicQueryTests
{
    [Fact]
    public void TopicQueryIncludesFixedSatisfactionWithoutIncludingOtherKnowledgeTopics()
    {
        var topic = Guid.NewGuid();
        var filter = DataverseService.BuildPublicSurveyQuestionFilter(topic.ToString());
        Assert.Equal($"(cr07a_componente eq 645250001 or _cr07a_tema_value eq {topic:D})", filter);
        // Inactive questions remain available for closed-session answer hydration.
        Assert.DoesNotContain("cr07a_activa", filter);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("invalid or true")]
    public void InvalidTopicCannotProduceAnUnscopedQuery(string topic) =>
        Assert.Throws<InvalidOperationException>(() => DataverseService.BuildPublicSurveyQuestionFilter(topic));

    [Fact]
    public void LegacySessionWithoutTopicKeepsOrphanQuestionsAndSatisfactionAccessible() =>
        Assert.Equal("(cr07a_componente eq 645250001 or _cr07a_tema_value eq null)",
            DataverseService.BuildPublicSurveyQuestionFilter(""));

    [Fact]
    public void EmptyQuestionListNeverLoadsTheOptionCatalog() =>
        Assert.Empty(DataverseService.BuildPublicSurveyOptionFilters(Array.Empty<string>()));

    [Fact]
    public void OptionsAreScopedDeduplicatedAndSplitIntoBoundedRequests()
    {
        var ids = Enumerable.Range(0, 121).Select(_ => Guid.NewGuid().ToString()).ToArray();
        var filters = DataverseService.BuildPublicSurveyOptionFilters(ids.Concat(ids.Take(3)));
        Assert.Equal(3, filters.Count);
        Assert.All(filters, filter => Assert.InRange(filter.Split(" or ").Length, 1, 50));
        var clauses = filters.SelectMany(filter => filter.Split(" or ")).ToArray();
        Assert.Equal(ids.Length, clauses.Length);
        Assert.All(ids, id => Assert.Contains($"_cr07a_pregunta_value eq {id}", clauses));
    }

    [Fact]
    public void InvalidQuestionCannotWidenTheOptionsQuery() =>
        Assert.Throws<InvalidOperationException>(() => DataverseService.BuildPublicSurveyOptionFilters(["x or true"]));
}
