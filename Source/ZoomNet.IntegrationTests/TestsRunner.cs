using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ZoomNet.IntegrationTests.Tests;
using ZoomNet.Models.Webhooks;

namespace ZoomNet.IntegrationTests
{
	internal class TestsRunner
	{
		private const int MAX_ZOOM_API_CONCURRENCY = 5;
		private const int TEST_NAME_MAX_LENGTH = 25;
		private const string SUCCESSFUL_TEST_MESSAGE = "Completed successfully";

		private enum ResultCodes
		{
			Success = 0,
			Exception = 1,
			Cancelled = 1223
		}

		private enum TestType
		{
			Api = 0,
			WebSockets = 1,
		}

		private enum ConnectionType
		{
			Jwt = 1,
			OAuth = 2,
		}

		private readonly ILoggerFactory _loggerFactory;

		public TestsRunner(ILoggerFactory loggerFactory)
		{
			_loggerFactory = loggerFactory;
		}

		public Task<int> RunAsync()
		{
			// -----------------------------------------------------------------------------
			// Do you want to proxy requests through Fiddler? Can be useful for debugging.
			var useFiddler = true;
			var fiddlerPort = 8888; // By default Fiddler4 uses port 8888 and Fiddler Everywhere uses port 8866

			// What tests do you want to run and which connection type do you want to use?
			var testType = TestType.Api;
			var connectionType = ConnectionType.OAuth;
			// -----------------------------------------------------------------------------

			// Ensure the Console is tall enough and centered on the screen
			if (OperatingSystem.IsWindows()) Console.WindowHeight = Math.Min(60, Console.LargestWindowHeight);
			ConsoleUtils.CenterConsole();

			// Configure the proxy if desired (very useful for debugging)
			var proxy = useFiddler ? new WebProxy($"http://localhost:{fiddlerPort}") : null;

			// Run tests either with a JWT or OAuth connection
			return connectionType switch
			{
				ConnectionType.Jwt => RunTestsWithJwtConnectionAsync(testType, proxy),
				ConnectionType.OAuth => RunTestsWithOAuthConnectionAsync(testType, proxy),
				_ => throw new Exception("Unknwon connection type"),
			};
		}

		private Task<int> RunTestsWithJwtConnectionAsync(TestType testType, IWebProxy proxy)
		{
			if (testType != TestType.Api) throw new Exception("Only API tests are supported with JWT");

			var apiKey = Environment.GetEnvironmentVariable("ZOOM_JWT_APIKEY", EnvironmentVariableTarget.User);
			var apiSecret = Environment.GetEnvironmentVariable("ZOOM_JWT_APISECRET", EnvironmentVariableTarget.User);
			var connectionInfo = new JwtConnectionInfo(apiKey, apiSecret);

			return RunApiTestsAsync(connectionInfo, proxy);
		}

		private Task<int> RunTestsWithOAuthConnectionAsync(TestType testType, IWebProxy proxy)
		{
			var clientId = Environment.GetEnvironmentVariable("ZOOM_OAUTH_CLIENTID", EnvironmentVariableTarget.User);
			var clientSecret = Environment.GetEnvironmentVariable("ZOOM_OAUTH_CLIENTSECRET", EnvironmentVariableTarget.User);
			var accountId = Environment.GetEnvironmentVariable("ZOOM_OAUTH_ACCOUNTID", EnvironmentVariableTarget.User);
			var refreshToken = Environment.GetEnvironmentVariable("ZOOM_OAUTH_REFRESHTOKEN", EnvironmentVariableTarget.User);
			var subscriptionId = Environment.GetEnvironmentVariable("ZOOM_WEBSOCKET_SUBSCRIPTIONID", EnvironmentVariableTarget.User);

			IConnectionInfo connectionInfo;

			// Server-to-Server OAuth
			if (!string.IsNullOrEmpty(accountId))
			{
				connectionInfo = OAuthConnectionInfo.ForServerToServer(clientId, clientSecret, accountId);
			}

			// Standard OAuth
			else
			{
				connectionInfo = OAuthConnectionInfo.WithRefreshToken(clientId, clientSecret, refreshToken,
					(newRefreshToken, newAccessToken) =>
					{
						Environment.SetEnvironmentVariable("ZOOM_OAUTH_REFRESHTOKEN", newRefreshToken, EnvironmentVariableTarget.User);
					});

				//var authorizationCode = "<-- the code generated by Zoom when the app is authorized by the user -->";
				//connectionInfo = OAuthConnectionInfo.WithAuthorizationCode(clientId, clientSecret, authorizationCode,
				//	(newRefreshToken, newAccessToken) =>
				//	{
				//		Environment.SetEnvironmentVariable("ZOOM_OAUTH_REFRESHTOKEN", newRefreshToken, EnvironmentVariableTarget.User);
				//	});
			}

			// Execute either the API or Websocket tests
			return testType switch
			{
				TestType.Api => RunApiTestsAsync(connectionInfo, proxy),
				TestType.WebSockets => RunWebSocketTestsAsync(connectionInfo, subscriptionId, proxy),
				_ => throw new Exception("Unknwon test type"),
			};
		}

		private async Task<int> RunApiTestsAsync(IConnectionInfo connectionInfo, IWebProxy proxy)
		{
			// Configure cancellation
			var cts = new CancellationTokenSource();
			Console.CancelKeyPress += (s, e) =>
			{
				e.Cancel = true;
				cts.Cancel();
			};

			// Configure ZoomNet client
			var client = new ZoomClient(connectionInfo, proxy, null, _loggerFactory.CreateLogger<ZoomClient>());

			// These are the integration tests that we will execute
			var integrationTests = new Type[]
			{
				typeof(Accounts),
				typeof(CallLogs),
				typeof(Chat),
				typeof(CloudRecordings),
				typeof(Contacts),
				typeof(Dashboards),
				typeof(Meetings),
				typeof(Roles),
				typeof(Users),
				typeof(Webinars),
				typeof(Reports),
				typeof(Workspaces),
			};

			// Get my user and permisisons
			var myUser = await client.Users.GetCurrentAsync(cts.Token).ConfigureAwait(false);
			var myPermissions = await client.Users.GetCurrentPermissionsAsync(cts.Token).ConfigureAwait(false);
			Array.Sort(myPermissions); // Sort permissions alphabetically for convenience

			// Execute the async tests in parallel (with max degree of parallelism)
			var results = await integrationTests.ForEachAsync(
				async testType =>
				{
					var log = new StringWriter();

					try
					{
						var integrationTest = (IIntegrationTest)Activator.CreateInstance(testType);
						await integrationTest.RunAsync(myUser, myPermissions, client, log, cts.Token).ConfigureAwait(false);
						return (TestName: testType.Name, ResultCode: ResultCodes.Success, Message: SUCCESSFUL_TEST_MESSAGE);
					}
					catch (OperationCanceledException)
					{
						await log.WriteLineAsync($"-----> TASK CANCELLED").ConfigureAwait(false);
						return (TestName: testType.Name, ResultCode: ResultCodes.Cancelled, Message: "Task cancelled");
					}
					catch (Exception e)
					{
						var exceptionMessage = e.GetBaseException().Message;
						await log.WriteLineAsync($"-----> AN EXCEPTION OCCURRED: {exceptionMessage}").ConfigureAwait(false);
						return (TestName: testType.Name, ResultCode: ResultCodes.Exception, Message: exceptionMessage);
					}
					finally
					{
						lock (Console.Out)
						{
							Console.Out.WriteLine(log.ToString());
						}
					}
				}, MAX_ZOOM_API_CONCURRENCY)
			.ConfigureAwait(false);

			// Display summary
			var summary = new StringWriter();
			await summary.WriteLineAsync("\n\n**************************************************").ConfigureAwait(false);
			await summary.WriteLineAsync("******************** SUMMARY *********************").ConfigureAwait(false);
			await summary.WriteLineAsync("**************************************************").ConfigureAwait(false);

			var nameMaxLength = Math.Min(results.Max(r => r.TestName.Length), TEST_NAME_MAX_LENGTH);
			foreach (var (TestName, ResultCode, Message) in results.OrderBy(r => r.TestName).ToArray())
			{
				await summary.WriteLineAsync($"{TestName.ToExactLength(nameMaxLength)} : {Message}").ConfigureAwait(false);
			}

			await summary.WriteLineAsync("**************************************************").ConfigureAwait(false);
			await Console.Out.WriteLineAsync(summary.ToString()).ConfigureAwait(false);

			// Prompt user to press a key in order to allow reading the log in the console
			var promptLog = new StringWriter();
			await promptLog.WriteLineAsync("\n\n**************************************************").ConfigureAwait(false);
			await promptLog.WriteLineAsync("Press any key to exit").ConfigureAwait(false);
			ConsoleUtils.Prompt(promptLog.ToString());

			// Return code indicating success/failure
			var resultCode = (int)ResultCodes.Success;
			if (results.Any(result => result.ResultCode != ResultCodes.Success))
			{
				if (results.Any(result => result.ResultCode == ResultCodes.Exception)) resultCode = (int)ResultCodes.Exception;
				else if (results.Any(result => result.ResultCode == ResultCodes.Cancelled)) resultCode = (int)ResultCodes.Cancelled;
				else resultCode = (int)results.First(result => result.ResultCode != ResultCodes.Success).ResultCode;
			}

			return resultCode;
		}

		private async Task<int> RunWebSocketTestsAsync(IConnectionInfo connectionInfo, string subscriptionId, IWebProxy proxy)
		{
			var logger = _loggerFactory.CreateLogger<ZoomWebSocketClient>();
			var eventProcessor = new Func<Event, CancellationToken, Task>(async (webhookEvent, cancellationToken) =>
			{
				if (!cancellationToken.IsCancellationRequested)
				{
					logger.LogInformation("Processing {eventType} event...", webhookEvent.EventType);
					await Task.Delay(1, cancellationToken).ConfigureAwait(false); // This async call gets rid of "CS1998 This async method lacks 'await' operators and will run synchronously".
				}
			});

			// Configure cancellation (this allows you to press CTRL+C or CTRL+Break to stop the websocket client)
			var cts = new CancellationTokenSource();
			var exitEvent = new ManualResetEvent(false);
			Console.CancelKeyPress += (s, e) =>
			{
				e.Cancel = true;
				cts.Cancel();
				exitEvent.Set();
			};

			// Start the websocket client
			using var client = new ZoomWebSocketClient(connectionInfo, subscriptionId, eventProcessor, proxy, logger);
			await client.StartAsync(cts.Token).ConfigureAwait(false);
			exitEvent.WaitOne();

			return (int)ResultCodes.Success;
		}
	}
}
