using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Core;

/// <summary>Registers reloadable canonical native settings and the legacy mail projection.</summary>
public static class DecisionConfigurationExtensions
{
    /// <summary>Resolves legacy-only settings on each reload and makes an explicit Decision section authoritative.</summary>
    public static IServiceCollection AddDecisionConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IOptionsChangeTokenSource<DecisionOptions>>(new ConfigurationChangeTokenSource<DecisionOptions>(configuration));
        services.AddOptions<DecisionOptions>().Configure(value =>
        {
            var section = configuration.GetSection(DecisionOptions.SectionName);
            if (DecisionOptions.HasCanonicalSection(configuration)) section.Bind(value);
            else
            {
                configuration.GetSection(GmailDecisionOptions.SectionName).Bind(value);
                value.Enabled = false;
            }
        }).Validate(DecisionOptions.IsValid, "Decision settings are invalid.");
        services.AddOptions<GmailDecisionOptions>()
            .Bind(configuration.GetSection(GmailDecisionOptions.SectionName))
            .Configure(mail =>
            {
                var value = DecisionOptions.Resolve(configuration);
                mail.DecisionProvider = value.DecisionProvider;
                mail.DecisionEndpoint = value.DecisionEndpoint;
                mail.DecisionModel = value.DecisionModel;
                mail.KeepAliveSeconds = value.KeepAliveSeconds;
                mail.UnloadAfterBatch = value.UnloadAfterBatch;
                mail.DecisionColdTimeoutSeconds = value.DecisionColdTimeoutSeconds;
                mail.DecisionWarmTimeoutSeconds = value.DecisionWarmTimeoutSeconds;
                mail.GateWaitTimeoutSeconds = value.GateWaitTimeoutSeconds;
                mail.MinTopProbabilityPercent = value.MinTopProbabilityPercent;
                mail.MinMarginPercentagePoints = value.MinMarginPercentagePoints;
                mail.MinConfidencePercent = value.MinConfidencePercent;
                mail.MinNoulCertaintyPercent = value.MinNoulCertaintyPercent;
            }).Validate(GmailDecisionOptions.IsValidMail, "Gmail decision settings are invalid.");
        return services;
    }
}
