using Filomena.Evaluation.Contracts;
using Npgsql;

namespace Filomena.Evaluation.Execution;

public sealed record EvaluationEndpointSettings(string Alias, string Provider, Uri Endpoint,
    Func<string?> Credential, CallReservation Reservation, TimeSpan RequestTimeout);

public sealed class BoundEvaluationProviders : IDisposable
{
    private readonly IReadOnlyList<HttpClient> clients;
    public EvaluationProviderFactory Factory { get; }

    private BoundEvaluationProviders(EvaluationProviderFactory factory, IReadOnlyList<HttpClient> clients)
    {
        Factory = factory;
        this.clients = clients;
    }

    public static BoundEvaluationProviders Create(NpgsqlDataSource source, EvaluationAccess access, Guid experiment,
        TrialClaim claim, IReadOnlyList<EvaluationEndpointSettings> endpoints)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(endpoints);
        access.Demand(EvaluationOperation.Evaluate);
        access.DemandScope(claim.Candidate.Scope);
        access.DemandScope(claim.Case.Scope);
        var clients = new List<HttpClient>();
        var bindings = new List<EvaluationEndpoint>();
        try
        {
            foreach (var endpoint in endpoints.ToArray())
            {
                ArgumentNullException.ThrowIfNull(endpoint);
                ArgumentNullException.ThrowIfNull(endpoint.Reservation);
                if (endpoint.RequestTimeout <= TimeSpan.Zero ||
                    endpoint.RequestTimeout.TotalMilliseconds > int.MaxValue ||
                    endpoint.Provider is "openai" or "jev" && endpoint.Reservation.Cost is null or <= 0)
                    throw new InvalidDataException("A finite timeout and positive paid-call reservation are required.");
                var journal = new AttemptLedger(source, access, experiment, claim.TrialId, claim.Owner,
                    claim.Generation, endpoint.Reservation);
                var handler = new EvaluationHttpHandler(endpoint.Provider, endpoint.Endpoint, journal)
                {
                    InnerHandler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }
                };
                var client = new HttpClient(handler) { Timeout = endpoint.RequestTimeout };
                clients.Add(client);
                bindings.Add(new(endpoint.Alias, endpoint.Provider, endpoint.Endpoint, client, endpoint.Credential));
            }
            return new(new EvaluationProviderFactory(bindings), clients);
        }
        catch
        {
            foreach (var client in clients) client.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var client in clients) client.Dispose();
    }
}
