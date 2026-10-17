using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Contracts.Exceptions;
using Lingarr.Contracts.Translation;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Services.Translation.Chain;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation.Chain;

/// <summary>
/// Guards the cross-line unusable-provider set of <see cref="ProofreadCandidateRunner"/>: a 401 on
/// the first line must skip every remaining model of that provider for the rest of the job.
/// </summary>
public class ProofreadCandidateRunnerTests
{
    [Fact]
    public async Task ProofreadAsync_FirstLineAuthenticationFailure_SecondLineDoesNotRetryThatProvider()
    {
        var unauthorized = MockFailingProofreader(new TranslationAuthenticationException("bad key"));
        var sameProviderSecondModel = MockProofreader("unused");
        var nextProvider = MockProofreader("fixed");
        var candidates = new List<TranslationServiceEntry>
        {
            Entry("custom", unauthorized, "model-a"),
            Entry("custom", sameProviderSecondModel, "model-b"),
            Entry("openai", nextProvider)
        };
        var unusableProviders = new HashSet<string>();

        var first = await ProofreadCandidateRunner.ProofreadAsync(
            candidates,
            "source one",
            "translated one",
            "en",
            "es",
            NullLogger.Instance,
            unusableProviders,
            CancellationToken.None);

        Assert.Equal("fixed", first?.Revision);
        Assert.Equal("openai", first?.Entry.Name);
        Assert.Contains("custom", unusableProviders);

        var second = await ProofreadCandidateRunner.ProofreadAsync(
            candidates,
            "source two",
            "translated two",
            "en",
            "es",
            NullLogger.Instance,
            unusableProviders,
            CancellationToken.None);

        Assert.Equal("fixed", second?.Revision);
        Assert.Equal("openai", second?.Entry.Name);
        unauthorized.As<IProofreadService>().Verify(service => service.ProofreadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        sameProviderSecondModel.As<IProofreadService>().Verify(service => service.ProofreadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        nextProvider.As<IProofreadService>().Verify(service => service.ProofreadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static TranslationServiceEntry Entry(
        string name,
        Mock<ITranslationService> service,
        string? model = null) =>
        new(name, service.Object, null, model);

    private static Mock<ITranslationService> MockProofreader(string revision)
    {
        var mock = new Mock<ITranslationService>();
        mock.As<IProofreadService>()
            .Setup(service => service.ProofreadAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(revision);
        return mock;
    }

    private static Mock<ITranslationService> MockFailingProofreader(Exception failure)
    {
        var mock = new Mock<ITranslationService>();
        mock.As<IProofreadService>()
            .Setup(service => service.ProofreadAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);
        return mock;
    }
}
