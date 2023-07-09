using DV.Logic.Job;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using DV.Booklets;
using DV.ThingTypes;
using DV.Utils;
using UnityEngine;

namespace DvMod.BookletOrganizer
{
    public static class BookletOrganizer
    {
        private enum OrganizerJobType
        {
            Unknown,
            Shunting,
            Transport,
        }

        private static OrganizerJobType GetOrganizerJobType(Job job)
        {
            return job.jobType switch
            {
                JobType.ShuntingLoad => OrganizerJobType.Shunting,
                JobType.ShuntingUnload => OrganizerJobType.Shunting,
                JobType.Transport => OrganizerJobType.Transport,
                JobType.EmptyHaul => OrganizerJobType.Transport,
                JobType.ComplexTransport => OrganizerJobType.Transport,
                _ => OrganizerJobType.Unknown,
            };
        }

        private static string DestinationStation(Job job) => job.chainData.chainDestinationYardId;

        private class BookletSpawnState
        {
            public int lastJobCount = 0;
            public float lastJobCountChangeTime = 0;

            private static readonly Dictionary<string, BookletSpawnState> instances = new Dictionary<string, BookletSpawnState>();
            public static BookletSpawnState Instance(string stationId)
            {
                if (!instances.TryGetValue(stationId, out var state))
                    state = instances[stationId] = new BookletSpawnState();
                return state;
            }
        }

        private const float PositionRandomizationRange = 0.01f;
        private const float RotationRandomizationRange = 2f;

        private const float XSpaceToUse = 0.95f;
        private const float ZSpaceToUse = 1f;
        private const float InitialJobXSpaceToUse = 0.8f;
        private const float MaxZSpacing = 0.2f;
        private const int MaxRowsPerType = 2;

        private static int GetJobsPerRow(int numJobs)
        {
            var minZSpacing = (float)MaxRowsPerType / numJobs;
            var zSpacing = Mathf.Min(minZSpacing, MaxZSpacing);
            return Mathf.CeilToInt(1f / zSpacing);
        }

        private static IEnumerable<IEnumerable<T>> Grouped<T>(this IEnumerable<T> enumerable, int groupSize)
        {
            var remaining = enumerable;
            do
            {
                yield return remaining.Take(groupSize);
                remaining = remaining.Skip(groupSize);
            } while (remaining.Any());
        }

        private static IEnumerable<(Job, float, float)> InitialBookletPositions(IEnumerable<Job> jobs)
        {
            var jobGroups = jobs
                .ToLookup(GetOrganizerJobType)
                .OrderBy(g => g.Key)
                .Select(g => g.OrderBy(DestinationStation));

            var jobsPerRowByGroup = jobGroups.Select(g => GetJobsPerRow(g.Count()));
            var rows = jobGroups.SelectMany(group => group.Grouped(GetJobsPerRow(group.Count())));
            Main.DebugLog(() => string.Join("\n", rows.Select(row => string.Join(",", row.Select(job => job.ID)))));

            var numRows = rows.Count();
            var xSpacing = numRows == 1 ? 0f : (InitialJobXSpaceToUse / (numRows - 1));
            IEnumerable<(Job job, float z, float x)> jobTuples = rows.SelectMany((jobsInRow, rowIndex) =>
            {
                var gapCount = jobsInRow.Count() - 1;
                var zSpacing = Mathf.Min(MaxZSpacing, gapCount == 0 ? 0f : 1f / gapCount);
                return jobsInRow.Select((job, indexInRow) => (job, indexInRow * zSpacing, rowIndex * xSpacing));
            });

            Main.DebugLog(() => string.Join(",", jobTuples.Select(t => $"({t.job.ID}->{t.job.chainData.chainDestinationYardId}@{t.z},{t.x})")));

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
                if (__instance.logicStation == null || !AStartGameData.carsAndJobsLoadingFinished)
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
                                __instance.jobBookletSpawnSurface.xSize * (0.5f - localX) * XSpaceToUse,
                                y,
                                __instance.jobBookletSpawnSurface.zSize * (0.5f - localZ) * ZSpaceToUse);
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