using DV.Logic.Job;
using DV.Utils;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DvMod.BookletOrganizer
{
    public static class BookletOrganizer
    {
        private class BookletSpawnState
        {
            public int lastJobCount = 0;
            public float lastJobCountChangeTime = 0;

            private static Dictionary<string, BookletSpawnState> instances = new Dictionary<string, BookletSpawnState>();
            public static BookletSpawnState Instance(string stationId)
            {
                if (!instances.TryGetValue(stationId, out var state))
                    state = instances[stationId] = new BookletSpawnState();
                return state;
            }
        }

        public const float PositionRandomizationRange = 0.01f;
        public const float RotationRandomizationRange = 2f;

        private static bool IsShuntingJob(Job job) =>
            job.jobType == JobType.ShuntingLoad || job.jobType == JobType.ShuntingUnload;
        private static string DestinationStation(Job job) =>
            job.chainData.chainDestinationYardId;

        private const float MaxXSpacing = 0.2f;
        private const float MaxYSpacing = 0.7f;
        private const float NumRows = 3;
        private static IEnumerable<(Job, float, float)> InitialBookletPositions(IEnumerable<Job> jobs)
        {
            var hasShuntingJobs = jobs.Any(IsShuntingJob);
            var shuntingJobs = jobs.Where(IsShuntingJob).OrderBy(job => job.chainData.chainDestinationYardId);
            var shuntingJobSpacing = Mathf.Min(MaxXSpacing, 1f / shuntingJobs.Count());
            Main.DebugLog(() => $"{shuntingJobs.Count()} shunting jobs, spacing={shuntingJobSpacing}");

            var transportJobs = jobs.Where(job => !IsShuntingJob(job)).OrderBy(job => job.chainData.chainDestinationYardId);
            var spacing = Mathf.Min(MaxXSpacing, (float)NumRows / (float)transportJobs.Count());
            var jobsPerRow = Mathf.FloorToInt(1f / spacing);
            var numRows = (hasShuntingJobs ? 1 : 0) + Mathf.Ceil((float)transportJobs.Count() / (float)jobsPerRow);
            Main.DebugLog(() => $"{transportJobs.Count()} transport jobs, spacing={spacing}, jobsPerRow={jobsPerRow}, numRows={numRows}");

            var ySpacing = Mathf.Min(MaxYSpacing, numRows == 1 ? 0f : (1f / (numRows - 1)));

            var shuntingJobTuples = shuntingJobs.Select((job, i) => (job, i * shuntingJobSpacing, 0f));
            var transportJobTuples = transportJobs
                .Select((job, i) => (job, i))
                .GroupBy(t => t.Item2 / jobsPerRow, t => t.Item1)
                .SelectMany(grouping => grouping.Select((job, i) => (job, i * spacing, (hasShuntingJobs ? grouping.Key + 1 : grouping.Key) * ySpacing)));
            
            var jobTuples = shuntingJobTuples.Concat(transportJobTuples);
            Main.DebugLog(() => string.Join(",", jobTuples.Select(t => $"({t.Item1.ID}->{t.Item1.chainData.chainDestinationYardId}@{t.Item2},{t.Item3})")));

            return jobTuples;
        }

        private static (Job, float, float) SecondaryBookletPosition(Job job)
        {
            return (job, Random.Range(0f, 1f), 1.0f);
        }

        [HarmonyPatch(typeof(StationController), nameof(StationController.Update))]
        public static class UpdatePatch
        {
            public static bool Prefix(StationController __instance)
            {
                if (__instance.logicStation == null || !SaveLoadController.carsAndJobsLoadingFinished)
                {
                    return false;
                }
                if (__instance.stationRange.IsPlayerInRangeForBookletGeneration(__instance.stationRange.PlayerSqrDistanceFromStationOffice) && __instance.attemptJobOverviewGeneration)
                {
                    var state = BookletSpawnState.Instance(__instance.logicStation.ID);
                    if (state.lastJobCount != __instance.logicStation.availableJobs.Count)
                    {
                        state.lastJobCount = __instance.logicStation.availableJobs.Count;
                        state.lastJobCountChangeTime = Time.time;
                        Main.DebugLog(() => $"Number of jobs changed at {__instance.logicStation.ID}");
                    }
                    else if (state.lastJobCountChangeTime > Time.time - 0.5f)
                    {
                        Main.DebugLog(() => $"Waiting to generate job booklets for {__instance.logicStation.ID}");
                    }
                    else
                    {
                        Main.DebugLog(() => $"Generating job booklets for {__instance.logicStation.ID}");
                        var isInitialGeneration = __instance.processedNewJobs.Count == 0;
                        var toGenerate = __instance.logicStation.availableJobs.Where(job => !__instance.processedNewJobs.Contains(job));

                        var jobTuples = isInitialGeneration ? InitialBookletPositions(toGenerate) : toGenerate.Select(SecondaryBookletPosition);
                        var parent = SingletonBehaviour<WorldMover>.Instance.originShiftParent;
                        var y = 0.001f;

                        foreach (var (job, z, x) in jobTuples)
                        {
                            var localX = x + Random.Range(-PositionRandomizationRange, PositionRandomizationRange);
                            var localZ = z + Random.Range(-PositionRandomizationRange, PositionRandomizationRange);
                            var localPosition = new Vector3(
                                -__instance.jobBookletSpawnSurface.xSize * (localX - 0.5f) * 0.9f,
                                y,
                                -__instance.jobBookletSpawnSurface.zSize * (localZ - 0.5f) * 0.9f);
                            Main.DebugLog(() => $"{job.ID} @ {localPosition}");
                            var globalPosition = __instance.jobBookletSpawnSurface.transform.TransformPoint(localPosition);
                            y += 0.001f;

                            var angle = Random.Range(-RotationRandomizationRange, RotationRandomizationRange);
                            var rotation = __instance.jobBookletSpawnSurface.transform.rotation * Quaternion.Euler(0f, -90f + angle, 0f);
                            JobOverview item = BookletCreator.CreateJobOverview(job, globalPosition, rotation, parent);
                            __instance.spawnedJobOverviews.Add(item);
                            __instance.processedNewJobs.Add(job);
                        }

                        __instance.attemptJobOverviewGeneration = false;
                    }
                }
                float playerSqrDistanceFromStationCenter = __instance.stationRange.PlayerSqrDistanceFromStationCenter;
                bool num = __instance.stationRange.IsPlayerInJobGenerationZone(playerSqrDistanceFromStationCenter);
                bool flag = __instance.stationRange.IsPlayerOutOfJobDestroyZone(playerSqrDistanceFromStationCenter, __instance.logicStation.takenJobs.Count > 0);
                if (num && !__instance.playerEnteredJobGenerationZone)
                {
                    __instance.ProceduralJobsController.TryToGenerateJobs();
                    __instance.playerEnteredJobGenerationZone = true;
                }
                else if (flag && __instance.playerEnteredJobGenerationZone)
                {
                    __instance.ProceduralJobsController.StopJobGeneration();
                    __instance.ExpireAllAvailableJobsInStation();
                    __instance.playerEnteredJobGenerationZone = false;
                }
                return false;
            }
        }
    }
}