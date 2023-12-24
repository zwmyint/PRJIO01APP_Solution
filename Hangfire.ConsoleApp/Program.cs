﻿using Hangfire.Client;
using Hangfire.Server;
using Hangfire.SqlServer;
using Hangfire.States;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using Hangfire.Common;

// https://github.com/HangfireIO/Hangfire/tree/master
// https://medium.com/codenx/explore-hangfire-with-net-c-e8fbfdefbb07

namespace Hangfire.ConsoleApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var host = new HostBuilder()
                .ConfigureLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Information))
                .ConfigureServices((hostContext, services) =>
                {
                    services.Configure<HostOptions>(option =>
                    {
                        option.ShutdownTimeout = TimeSpan.FromSeconds(60);
                    });

                    services.TryAddSingleton<SqlServerStorageOptions>(new SqlServerStorageOptions
                    {
                        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                        QueuePollInterval = TimeSpan.FromTicks(1),
                        UseRecommendedIsolationLevel = true,
                        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(1)
                    });

                    services.TryAddSingleton<IBackgroundJobFactory>(x => new CustomBackgroundJobFactory(
                        new BackgroundJobFactory(x.GetRequiredService<IJobFilterProvider>())));

                    services.TryAddSingleton<IBackgroundJobPerformer>(x => new CustomBackgroundJobPerformer(
                        new BackgroundJobPerformer(
                            x.GetRequiredService<IJobFilterProvider>(),
                            x.GetRequiredService<JobActivator>(),
                            TaskScheduler.Default)));

                    services.TryAddSingleton<IBackgroundJobStateChanger>(x => new CustomBackgroundJobStateChanger(
                            new BackgroundJobStateChanger(x.GetRequiredService<IJobFilterProvider>())));

                    services.AddHangfire((provider, configuration) => configuration
                        .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
                        .UseSimpleAssemblyNameTypeSerializer()
                        .UseSqlServerStorage(
                            @"Server=MAXIMUS-XI\\SQLDEV2017;Database=SQL2017_TESTDB2; User Id=sa; Password=ERPfegha1730; Trusted_Connection=false; MultipleActiveResultSets=true; Persist Security Info=True;",
                            provider.GetRequiredService<SqlServerStorageOptions>()));

                    services.AddHostedService<RecurringJobsService>();
                    services.AddHangfireServer(options =>
                    {
                        options.StopTimeout = TimeSpan.FromSeconds(15);
                        options.ShutdownTimeout = TimeSpan.FromSeconds(30);
                    });
                })
                .Build();

            await host.RunAsync();

        }

        //
    }

    //
    internal class CustomBackgroundJobFactory : IBackgroundJobFactory
    {
        private readonly IBackgroundJobFactory _inner;

        public CustomBackgroundJobFactory([NotNull] IBackgroundJobFactory inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IStateMachine StateMachine => _inner.StateMachine;

        public BackgroundJob Create(CreateContext context)
        {
            Console.WriteLine($"Create: {context.Job.Type.FullName}.{context.Job.Method.Name} in {context.InitialState?.Name} state");
            return _inner.Create(context);
        }
    }

    internal class CustomBackgroundJobPerformer : IBackgroundJobPerformer
    {
        private readonly IBackgroundJobPerformer _inner;

        public CustomBackgroundJobPerformer([NotNull] IBackgroundJobPerformer inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public object Perform(PerformContext context)
        {
            Console.WriteLine($"Perform {context.BackgroundJob.Id} ({context.BackgroundJob.Job.Type.FullName}.{context.BackgroundJob.Job.Method.Name})");
            return _inner.Perform(context);
        }
    }

    internal class CustomBackgroundJobStateChanger : IBackgroundJobStateChanger
    {
        private readonly IBackgroundJobStateChanger _inner;

        public CustomBackgroundJobStateChanger([NotNull] IBackgroundJobStateChanger inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IState ChangeState(StateChangeContext context)
        {
            Console.WriteLine($"ChangeState {context.BackgroundJobId} to {context.NewState}");
            return _inner.ChangeState(context);
        }
    }


    internal class RecurringJobsService : BackgroundService
    {
        private readonly IBackgroundJobClient _backgroundJobs;
        private readonly IRecurringJobManager _recurringJobs;
        private readonly ILogger<RecurringJobScheduler> _logger;

        public RecurringJobsService(
            [NotNull] IBackgroundJobClient backgroundJobs,
            [NotNull] IRecurringJobManager recurringJobs,
            [NotNull] ILogger<RecurringJobScheduler> logger)
        {
            _backgroundJobs = backgroundJobs ?? throw new ArgumentNullException(nameof(backgroundJobs));
            _recurringJobs = recurringJobs ?? throw new ArgumentNullException(nameof(recurringJobs));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                Console.WriteLine("Hangfire Server started. ...");

                // Fire-and-forget tasks
                string fileName = "xyz.txt";
                _backgroundJobs.Enqueue(() => Console.WriteLine("Simple !"));
                _backgroundJobs.Enqueue<Services>(x => x.LongRunning(JobCancellationToken.Null));
                _backgroundJobs.Enqueue(() => LogFileUpload(fileName));

                // Delayed tasks
                // Scheduled background jobs are executed only after a given amount of time.
                _backgroundJobs.Schedule(() => Console.WriteLine("Reliable !"), TimeSpan.FromDays(1));
                _backgroundJobs.Schedule(() => AnalyzeFile(fileName), TimeSpan.FromMinutes(10));

                // Recurring tasks
                _recurringJobs.AddOrUpdate("seconds", () => Console.WriteLine("Hello, seconds!"), "*/15 * * * * *");
                _recurringJobs.AddOrUpdate("minutely", () => Console.WriteLine("Hello, world!"), Cron.Minutely);
                _recurringJobs.AddOrUpdate("hourly", () => Console.WriteLine("Hello"), "25 15 * * *");
                _recurringJobs.AddOrUpdate("neverfires", () => Console.WriteLine("Can only be triggered"), "0 0 31 2 *");

                _recurringJobs.AddOrUpdate("Hawaiian", () => Console.WriteLine("Hawaiian"), "15 08 * * *", new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Hawaiian Standard Time")
                });
                _recurringJobs.AddOrUpdate("UTC", () => Console.WriteLine("UTC"), "15 18 * * *");
                _recurringJobs.AddOrUpdate("Russian", () => Console.WriteLine("Russian"), "15 21 * * *", new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                });

                _recurringJobs.AddOrUpdate(
                    "daily-file-cleanup",
                    () => PerformDailyCleanup(),
                    Cron.Daily);


                //// Continuations
                //var id = _backgroundJobs.Enqueue(() => Console.WriteLine("Hello, "));
                //_backgroundJobs.ContinueWith(id, () => Console.WriteLine("world!"));

            }
            catch (Exception e)
            {
                _logger.LogError(e, "An exception occurred while creating recurring jobs.");
            }

            return Task.CompletedTask;
        }


        public void LogFileUpload(string fileName)
        {
            // Logic to log the file upload
            Console.WriteLine($"File uploaded: {fileName}");
        }

        public void AnalyzeFile(string fileName)
        {
            // Logic to analyze the file
            Console.WriteLine($"Analyzing file: {fileName}");
        }

        public void PerformDailyCleanup()
        {
            // Logic for daily cleanup
            Console.WriteLine("Performing daily file system cleanup.");
        }

        //
    }




    //

}
