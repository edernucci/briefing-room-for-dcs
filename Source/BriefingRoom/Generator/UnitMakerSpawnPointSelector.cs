﻿/*
==========================================================================
This file is part of Briefing Room for DCS World, a mission
generator for DCS World, by @akaAgar (https://github.com/akaAgar/briefing-room-for-dcs)

Briefing Room for DCS World is free software: you can redistribute it
and/or modify it under the terms of the GNU General Public License
as published by the Free Software Foundation, either version 3 of
the License, or (at your option) any later version.

Briefing Room for DCS World is distributed in the hope that it will
be useful, but WITHOUT ANY WARRANTY; without even the implied warranty
of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with Briefing Room for DCS World. If not, see https://www.gnu.org/licenses/
==========================================================================
*/

using BriefingRoom4DCS.Data;
using BriefingRoom4DCS.Template;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BriefingRoom4DCS.Generator
{
    internal class UnitMakerSpawnPointSelector
    {
        private const int MAX_RADIUS_SEARCH_ITERATIONS = 15;
        private readonly List<UnitCategory> NEAR_FRONT_LINE_CATEGORIES = new() { UnitCategory.Static, UnitCategory.Vehicle, UnitCategory.Infantry};

        private readonly Dictionary<int, List<DBEntryAirbaseParkingSpot>> AirbaseParkingSpots;

        private readonly List<DBEntryTheaterSpawnPoint> SpawnPoints;
        internal readonly List<DBEntryTheaterSpawnPoint> UsedSpawnPoints;

        private readonly DBEntryTheater TheaterDB;

        private readonly DBEntrySituation SituationDB;

        private readonly int MinBorderLimit;

        private readonly bool InvertCoalition;

        private readonly List<UnitFamily> LARGE_AIRCRAFT = new()
        {
                UnitFamily.PlaneAWACS,
                UnitFamily.PlaneTankerBasket,
                UnitFamily.PlaneTankerBoom,
                UnitFamily.PlaneTransport,
                UnitFamily.PlaneBomber,
            };

        private List<Coordinates> FrontLine = new(); 
        private bool PlayerSideOfFrontLine;

        private Coalition PlayerCoalition;

        internal UnitMakerSpawnPointSelector(DBEntryTheater theaterDB, DBEntrySituation situationDB, bool invertCoalition, int minBorderLimit)
        {
            TheaterDB = theaterDB;
            SituationDB = situationDB;
            AirbaseParkingSpots = new Dictionary<int, List<DBEntryAirbaseParkingSpot>>();
            SpawnPoints = new List<DBEntryTheaterSpawnPoint>();
            UsedSpawnPoints = new List<DBEntryTheaterSpawnPoint>();
            InvertCoalition = invertCoalition;
            MinBorderLimit = minBorderLimit;

            if (TheaterDB.SpawnPoints is not null)
                SpawnPoints.AddRange(TheaterDB.SpawnPoints.Where(x => CheckNotInNoSpawnCoords(x.Coordinates)).ToList());
            
            var brokenSP = SpawnPoints.Where(x => CheckInSea(x.Coordinates)).ToList();
            if (brokenSP.Count > 0)
                throw new BriefingRoomException($"Spawn Points in the sea!: {string.Join("\n", brokenSP.Select(x => $"[{x.Coordinates.X},{x.Coordinates.Y}],{x.PointType}").ToList())}");

            foreach (DBEntryAirbase airbase in SituationDB.GetAirbases(InvertCoalition))
            {
                if (airbase.ParkingSpots.Length < 1) continue;
                if (AirbaseParkingSpots.ContainsKey(airbase.DCSID)) continue;
                AirbaseParkingSpots.Add(airbase.DCSID, airbase.ParkingSpots.ToList());
            }
        }


        internal List<DBEntryAirbaseParkingSpot> GetFreeParkingSpots(int airbaseID, int unitCount, DBEntryAircraft aircraftDB, bool requiresOpenAirParking = false)
        {

            if (!AirbaseParkingSpots.ContainsKey(airbaseID))
                throw new BriefingRoomException($"Airbase {airbaseID} not found in parking map");


            var airbaseDB = SituationDB.GetAirbases(InvertCoalition).First(x => x.DCSID == airbaseID);
            var parkingSpots = new List<DBEntryAirbaseParkingSpot>();
            DBEntryAirbaseParkingSpot? lastSpot = null;
            for (int i = 0; i < unitCount; i++)
            {
                var viableSpots = FilterAndSortSuitableSpots(AirbaseParkingSpots[airbaseID].ToArray(), aircraftDB, requiresOpenAirParking);
                if (viableSpots.Count == 0) throw new BriefingRoomException($"Airbase {airbaseDB.UIDisplayName.Get()} didn't have enough suitable parking spots.");
                var parkingSpot = viableSpots.First();
                if (lastSpot.HasValue) //find nearest spot distance wise in attempt to cluster
                    parkingSpot = viableSpots
                        .Aggregate((acc, x) => acc.Coordinates.GetDistanceFrom(lastSpot.Value.Coordinates) > x.Coordinates.GetDistanceFrom(lastSpot.Value.Coordinates) ? x : acc);

                lastSpot = parkingSpot;
                AirbaseParkingSpots[airbaseID].Remove(parkingSpot);
                parkingSpots.Add(parkingSpot);
            }

            return parkingSpots;
        }

        internal Coordinates? GetNearestSpawnPoint(
            SpawnPointType[] validTypes,
            Coordinates origin, bool remove = true)
        {
            if (validTypes.Contains(SpawnPointType.Air) || validTypes.Contains(SpawnPointType.Sea))
                return Coordinates.CreateRandom(origin, new MinMaxD(1 * Toolbox.NM_TO_METERS, 3 * Toolbox.NM_TO_METERS));
            var sp = SpawnPoints.Where(x => validTypes.Contains(x.PointType)).Aggregate((acc, x) => origin.GetDistanceFrom(x.Coordinates) < origin.GetDistanceFrom(acc.Coordinates) ? x : acc);
            if (remove)
            {
                SpawnPoints.Remove(sp);
                UsedSpawnPoints.Add(sp);
            }
            return sp.Coordinates;
        }

        internal Coordinates? GetRandomSpawnPoint(
            SpawnPointType[] validTypes,
            Coordinates distanceOrigin1, MinMaxD distanceFrom1,
            Coordinates? distanceOrigin2 = null, MinMaxD? distanceFrom2 = null,
            Coalition? coalition = null,
            UnitFamily? nearFrontLineFamily = null)
        {
            if (validTypes.Contains(SpawnPointType.Air) || validTypes.Contains(SpawnPointType.Sea))
                return GetAirOrSeaCoordinates(validTypes, distanceOrigin1, distanceFrom1, distanceOrigin2, distanceFrom2, coalition);
            return GetLandCoordinates(validTypes, distanceOrigin1, distanceFrom1, distanceOrigin2, distanceFrom2, coalition, nearFrontLineFamily);
        }

        private Coordinates? GetLandCoordinates(
            SpawnPointType[] validTypes,
            Coordinates distanceOrigin1, MinMaxD distanceFrom1,
            Coordinates? distanceOrigin2 = null, MinMaxD? distanceFrom2 = null,
            Coalition? coalition = null,
            UnitFamily? nearFrontLineFamily = null,
            bool nested = false
        )
        {
            var validSP = (from DBEntryTheaterSpawnPoint pt in SpawnPoints where validTypes.Contains(pt.PointType) select pt);
            Coordinates?[] distanceOrigin = new Coordinates?[] { distanceOrigin1, distanceOrigin2 };
            MinMaxD?[] distanceFrom = new MinMaxD?[] { distanceFrom1, distanceFrom2 };
            var useFrontLine = nearFrontLineFamily.HasValue && FrontLine.Count  > 0 && NEAR_FRONT_LINE_CATEGORIES.Contains(nearFrontLineFamily.Value.GetUnitCategory());
            for (int i = 0; i < 2; i++) // Remove spawn points too far or too close from distanceOrigin1 and distanceOrigin2
            {
                if (!validSP.Any()) break;
                if (!distanceFrom[i].HasValue || !distanceOrigin[i].HasValue) continue;

                var borderLimit = (double)MinBorderLimit;
                var searchRange = distanceFrom[i].Value * Toolbox.NM_TO_METERS; // convert distance to meters

                IEnumerable<DBEntryTheaterSpawnPoint> validSPInRange = (from DBEntryTheaterSpawnPoint s in validSP select s);

                int iterationsLeft = MAX_RADIUS_SEARCH_ITERATIONS;

                do
                {
                    Coordinates origin = distanceOrigin[i].Value;

                    validSPInRange = (from DBEntryTheaterSpawnPoint s in validSP
                                      where
                                          searchRange.Contains(origin.GetDistanceFrom(s.Coordinates)) &&
                                          CheckNotInHostileCoords(s.Coordinates, coalition) &&
                                          (useFrontLine ? CheckNotFarFromFrontLine(s.Coordinates, nearFrontLineFamily.Value, coalition) : CheckNotFarFromBorders(s.Coordinates, borderLimit, coalition))
                                      select s);
                    searchRange = new MinMaxD(searchRange.Min * 0.95, searchRange.Max * 1.05);
                    validSP = (from DBEntryTheaterSpawnPoint s in validSPInRange select s);
                    if (iterationsLeft < MAX_RADIUS_SEARCH_ITERATIONS * 0.3)
                        borderLimit *= 1.05;
                    iterationsLeft--;
                } while ((!validSPInRange.Any()) && (iterationsLeft > 0));
            }

            if (!validSP.Any())
                return !coalition.HasValue && (useFrontLine || nested) ? null : GetLandCoordinates(validTypes, distanceOrigin1, distanceFrom1, distanceOrigin2, distanceFrom2, null, nearFrontLineFamily, true);
            DBEntryTheaterSpawnPoint selectedSpawnPoint = Toolbox.RandomFrom(validSP.ToArray());
            SpawnPoints.Remove(selectedSpawnPoint); // Remove spawn point so it won't be used again;
            UsedSpawnPoints.Add(selectedSpawnPoint);
            return selectedSpawnPoint.Coordinates;
        }

        private Coordinates? GetAirOrSeaCoordinates(
            SpawnPointType[] validTypes,
            Coordinates distanceOrigin1, MinMaxD distanceFrom1,
            Coordinates? distanceOrigin2 = null, MinMaxD? distanceFrom2 = null,
            Coalition? coalition = null)
        {
            var searchRange = distanceFrom1 * Toolbox.NM_TO_METERS;
            var borderLimit = (double)MinBorderLimit;
            MinMaxD? secondSearchRange = null;
            if (distanceOrigin2.HasValue && distanceFrom2.HasValue)
            {
                secondSearchRange = distanceFrom2.Value * Toolbox.NM_TO_METERS;
            }

            var iterations = 0;
            do
            {
                var coordOptionsLinq = Enumerable.Range(0, 5000)
                    .Select(x => Coordinates.CreateRandom(distanceOrigin1, searchRange))
                    .Where(x => CheckNotInHostileCoords(x, coalition) && CheckNotInNoSpawnCoords(x) && CheckNotFarFromBorders(x, borderLimit, coalition));

                if (secondSearchRange.HasValue)
                    coordOptionsLinq = coordOptionsLinq.Where(x => secondSearchRange.Value.Contains(distanceOrigin2.Value.GetDistanceFrom(x)));

                if (validTypes.First() == SpawnPointType.Sea) //sea position
                    coordOptionsLinq = coordOptionsLinq.Where(x => CheckInSea(x));

                var coordOptions = coordOptionsLinq.ToList();
                if (coordOptions.Count > 0)
                    return Toolbox.RandomFrom(coordOptions);

                searchRange = new MinMaxD(searchRange.Min * 0.95, searchRange.Max * 1.15);

                if (secondSearchRange.HasValue)
                    secondSearchRange = new MinMaxD(secondSearchRange.Value.Min * 0.95, secondSearchRange.Value.Max * 1.05);

                if (iterations > MAX_RADIUS_SEARCH_ITERATIONS * 0.66)
                    borderLimit *= 1.05;

                iterations++;
            } while (iterations < MAX_RADIUS_SEARCH_ITERATIONS);

            return null;
        }

        internal Tuple<DBEntryAirbase, List<int>, List<Coordinates>> GetAirbaseAndParking(
            MissionTemplateRecord template, Coordinates coordinates,
            int unitCount, Coalition coalition, DBEntryAircraft aircraftDB)
        {
            var targetAirbaseOptions =
                        (from DBEntryAirbase airbaseDB in SituationDB.GetAirbases(template.OptionsMission.Contains("InvertCountriesCoalitions"))
                         where (coalition == Coalition.Neutral || airbaseDB.Coalition == coalition) && AirbaseParkingSpots.ContainsKey(airbaseDB.DCSID) && ValidateAirfieldParking(AirbaseParkingSpots[airbaseDB.DCSID], aircraftDB.Families.First(), unitCount) && ValidateAirfieldRunway(airbaseDB, aircraftDB.Families.First())
                         select airbaseDB).OrderBy(x => x.Coordinates.GetDistanceFrom(coordinates));

            if (!targetAirbaseOptions.Any()) throw new BriefingRoomException("No airbase found for aircraft.");

            List<DBEntryAirbaseParkingSpot> parkingSpots;
            foreach (var airbase in targetAirbaseOptions)
            {
                try
                {
                    parkingSpots = GetFreeParkingSpots(airbase.DCSID, unitCount, aircraftDB);
                }
                catch (BriefingRoomException)
                {
                    continue;
                }

                return Tuple.Create(airbase, parkingSpots.Select(x => x.DCSID).ToList(), parkingSpots.Select(x => x.Coordinates).ToList());
            }
            throw new BriefingRoomException("No airbase found with sufficient parking spots.");
        }

        internal void RecoverSpawnPoint(Coordinates coords)
        {
            var usedSP = UsedSpawnPoints.Find(x => x.Coordinates.X == coords.X && x.Coordinates.Y == x.Coordinates.Y);
            if(usedSP.Coordinates.ToString() == Coordinates.Zero.ToString())
                return;
            SpawnPoints.Add(usedSP);
        }

        internal double GetDirToFrontLine(Coordinates coords)
        {
            var nearestFrontLinePoint = ShapeManager.GetNearestPointBorder(coords, FrontLine);
            return nearestFrontLinePoint.Item2.GetHeadingFrom(coords);
        }

        private List<DBEntryAirbaseParkingSpot> FilterAndSortSuitableSpots(DBEntryAirbaseParkingSpot[] parkingspots, DBEntryAircraft aircraftDB, bool requiresOpenAirParking)
        {
            if (parkingspots.Any(x => x.Height == 0))
            {
                BriefingRoom.PrintToLog("Using Simplified parking logic units may overlap", LogMessageErrorLevel.Warning);
                return FilterAndSortSuitableSpotsSimple(parkingspots, aircraftDB.Families.First(), requiresOpenAirParking);
            }
            var category = aircraftDB.Families.First().GetUnitCategory();
            var opts = parkingspots.Where(x =>
                aircraftDB.Height < x.Height
                && aircraftDB.Length < x.Length
                && aircraftDB.Width < x.Width
                && (!requiresOpenAirParking || x.ParkingType != ParkingSpotType.HardenedAirShelter)
             );
            if (category == UnitCategory.Helicopter)
                return opts.Where(x => x.ParkingType != ParkingSpotType.AirplaneOnly || x.ParkingType != ParkingSpotType.HardenedAirShelter).ToList();
            return opts.Where(x => x.ParkingType != ParkingSpotType.HelicopterOnly).ToList();
        }

        private List<DBEntryAirbaseParkingSpot> FilterAndSortSuitableSpotsSimple(DBEntryAirbaseParkingSpot[] parkingspots, UnitFamily unitFamily, bool requiresOpenAirParking)
        {
            var validTypes = new List<ParkingSpotType>{
                ParkingSpotType.OpenAirSpawn,
                ParkingSpotType.HardenedAirShelter,
                ParkingSpotType.AirplaneOnly
            };

            if (unitFamily.GetUnitCategory() == UnitCategory.Helicopter)
                validTypes = new List<ParkingSpotType>{
                    ParkingSpotType.OpenAirSpawn,
                    ParkingSpotType.HelicopterOnly,
                };
            else if (IsBunkerUnsuitable(unitFamily) || requiresOpenAirParking)
                validTypes = new List<ParkingSpotType>{
                    ParkingSpotType.OpenAirSpawn
                };

            return parkingspots.Where(x => validTypes.Contains(x.ParkingType)).OrderBy(x => x.ParkingType).ToList();
        }

        private bool IsBunkerUnsuitable(UnitFamily unitFamily) =>
           LARGE_AIRCRAFT.Contains(unitFamily) || unitFamily.GetUnitCategory() == UnitCategory.Helicopter;

        private bool ValidateAirfieldParking(List<DBEntryAirbaseParkingSpot> parkingSpots, UnitFamily unitFamily, int unitCount)
        {
            var openSpots = parkingSpots.Count(X => X.ParkingType == ParkingSpotType.OpenAirSpawn);
            if (openSpots >= unitCount) //Is there just enough open spaces
                return true;

            // Helicopters
            if (unitFamily.GetUnitCategory() == UnitCategory.Helicopter)
                return parkingSpots.Count(X => X.ParkingType == ParkingSpotType.HelicopterOnly) + openSpots > unitCount;

            // Aircraft that can't use bunkers
            if (IsBunkerUnsuitable(unitFamily))
                return parkingSpots.Count(X => X.ParkingType == ParkingSpotType.AirplaneOnly) + openSpots > unitCount;

            // Bunkerable aircraft
            return parkingSpots.Count(X => X.ParkingType == ParkingSpotType.HardenedAirShelter) + openSpots > unitCount;
        }

        private bool ValidateAirfieldRunway(DBEntryAirbase airbaseDB, UnitFamily unitFamily)
        {
            if (airbaseDB.RunwayLengthFt == -1 || !LARGE_AIRCRAFT.Contains(unitFamily)) //TODO implement runway distances on all relavant airbases
                return true;
            return airbaseDB.RunwayLengthFt > 7000; //TODO This is a guess based on most runways I know work so far. Place holder for per aircraft data
        }

        private bool CheckNotInHostileCoords(Coordinates coordinates, Coalition? coalition = null)
        {
            if (!coalition.HasValue)
                return true;

            var red = SituationDB.GetRedZones(InvertCoalition);
            var blue = SituationDB.GetBlueZones(InvertCoalition);

            return !ShapeManager.IsPosValid(coordinates, (coalition.Value == Coalition.Blue ? red : blue));
        }

        private bool CheckNotInNoSpawnCoords(Coordinates coordinates)
        {
            if (SituationDB.NoSpawnZones.Count == 0)
                return true;
            return !ShapeManager.IsPosValid(coordinates, SituationDB.NoSpawnZones);
        }

        private bool CheckNotFarFromBorders(Coordinates coordinates, double borderLimit, Coalition? coalition = null)
        {
            if (!coalition.HasValue)
                return true;

            var red = SituationDB.GetRedZones(InvertCoalition);
            var blue = SituationDB.GetBlueZones(InvertCoalition);

            var distanceLimit = Toolbox.NM_TO_METERS * borderLimit;
            var selectedZones = coalition.Value == Coalition.Blue ? blue : red;
            var distance = selectedZones.Min(x => ShapeManager.GetDistanceFromShape(coordinates, x));
            return distance < distanceLimit;

        }

        private bool CheckNotFarFromFrontLine(Coordinates coordinates, UnitFamily unitFamily, Coalition? coalition = null)
        {
            if (!coalition.HasValue)
                return true;
            var distance = ShapeManager.GetDistanceFromShape(coordinates, FrontLine);
            var side = ShapeManager.GetSideOfLine(coordinates, FrontLine);

            var onPlayerCoalition = coalition == PlayerCoalition;
            var onFriendlySideOfLine = (onPlayerCoalition && side == PlayerSideOfFrontLine) || (!onPlayerCoalition && side != PlayerSideOfFrontLine);

            var frontLineDB = Database.Instance.Common.FrontLine;

            var onFriendlySideOfLineIndex = onFriendlySideOfLine ? 0 : 1;
            var distanceLimit = frontLineDB.DefaultUnitLimits[onFriendlySideOfLineIndex];
            if(frontLineDB.UnitLimits.ContainsKey(unitFamily))
                distanceLimit = frontLineDB.UnitLimits[unitFamily][onFriendlySideOfLineIndex];

            return (distance * Toolbox.METERS_TO_NM) < distanceLimit;

        }

        internal bool CheckInSea(Coordinates coordinates)
        {
            return TheaterDB.WaterCoordinates.Any(x => ShapeManager.IsPosValid(coordinates, x, TheaterDB.WaterExclusionCoordinates));
        }

        internal void SetFrontLine(List<Coordinates> frontLine, Coordinates playerAirbase, Coalition playerCoalition)
        {
            FrontLine = frontLine;
            PlayerSideOfFrontLine = ShapeManager.GetSideOfLine(playerAirbase, FrontLine);
            PlayerCoalition = playerCoalition;
        }
    }
}
