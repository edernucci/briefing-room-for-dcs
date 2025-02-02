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
using BriefingRoom4DCS.Mission;
using BriefingRoom4DCS.Template;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BriefingRoom4DCS.Generator
{
    internal class MissionGeneratorCarrierGroup
    {


        internal static void GenerateCarrierGroup(
            UnitMaker unitMaker, ZoneMaker zoneMaker, DCSMission mission, MissionTemplateRecord template,
            Coordinates landbaseCoordinates, Coordinates objectivesCenter, double windSpeedAtSeaLevel,
            double windDirectionAtSeaLevel)
        {
            DBEntryTheater theaterDB = Database.Instance.GetEntry<DBEntryTheater>(template.ContextTheater);
            double carrierSpeed = Math.Max(
                    Database.Instance.Common.CarrierGroup.MinimumCarrierSpeed,
                    Database.Instance.Common.CarrierGroup.IdealWindOfDeck - windSpeedAtSeaLevel);
            if (windSpeedAtSeaLevel == 0) // No wind? Pick a random direction so carriers don't always go to a 0 course when wind is calm.
                windDirectionAtSeaLevel = Toolbox.RandomDouble(Toolbox.TWO_PI);
            var carrierPathDeg = ((windDirectionAtSeaLevel + Math.PI) % Toolbox.TWO_PI) * Toolbox.RADIANS_TO_DEGREES;
            var usedCoordinates = new List<Coordinates>();
            var templatesDB = Database.Instance.GetAllEntries<DBEntryTemplate>();
            foreach (MissionTemplateFlightGroupRecord flightGroup in template.PlayerFlightGroups)
            {
                if (string.IsNullOrEmpty(flightGroup.Carrier) || unitMaker.CarrierDictionary.ContainsKey(flightGroup.Carrier)) continue;
                if (templatesDB.Where(x => x.Type == "FOB").Any(x => x.ID == flightGroup.Carrier))
                {
                    //It Carries therefore carrier not because I can't think of a name to rename this lot
                    GenerateFOB(unitMaker, zoneMaker, flightGroup, mission, template, landbaseCoordinates, objectivesCenter);
                    continue;
                }
                var initalUnitDB = Database.Instance.GetEntry<DBEntryJSONUnit>(flightGroup.Carrier);
                var unitDB = (DBEntryShip)initalUnitDB;
                if ((unitDB == null) || !unitDB.Families.Any(x => x.IsCarrier())) continue; // Unit doesn't exist or is not a carrier

                var (shipCoordinates, shipDestination) = GetSpawnAndDestination(unitMaker, template, theaterDB, usedCoordinates, landbaseCoordinates, objectivesCenter, carrierPathDeg, flightGroup);
                usedCoordinates.Add(shipCoordinates);
                string cvnID = unitMaker.CarrierDictionary.Count > 0 ? (unitMaker.CarrierDictionary.Count + 1).ToString() : "";
                int ilsChannel = 11 + unitMaker.CarrierDictionary.Count;
                int link4Frequency = 336 + unitMaker.CarrierDictionary.Count;
                double radioFrequency = 127.5 + unitMaker.CarrierDictionary.Count;
                string tacanCallsign = $"CVN{cvnID}";
                int tacanChannel = 74 + unitMaker.CarrierDictionary.Count;
                var extraSettings = new Dictionary<string, object>{
                        {"GroupX2", shipDestination.X},
                        {"GroupY2", shipDestination.Y},
                        {"ILS", ilsChannel},
                        {"RadioBand", (int)RadioModulation.AM},
                        {"RadioFrequency", GeneratorTools.GetRadioFrequency(radioFrequency)},
                        {"Speed", carrierSpeed},
                        {"TACANCallsign", tacanCallsign},
                        {"TACANChannel", tacanChannel},
                        {"TACANFrequency", GeneratorTools.GetTACANFrequency(tacanChannel, 'X', false)},
                        {"Link4Frequency", GeneratorTools.GetRadioFrequency(link4Frequency)},
                        {"TACANMode"," X"},
                        {"playerCanDrive", false},
                        {"NoCM", true}};
                var templateOps = templatesDB.Where(x => x.Units.First().DCSID == unitDB.DCSID).ToList();
                UnitMakerGroupInfo? groupInfo;
                var groupLua = "ShipCarrier";
                var unitLua = "Ship";
                if (templateOps.Count > 0)
                    groupInfo = unitMaker.AddUnitGroupTemplate(Toolbox.RandomFrom(templateOps), Side.Ally, groupLua, unitLua, shipCoordinates, 0, extraSettings);
                else
                    groupInfo = unitMaker.AddUnitGroup(unitDB.DCSID, Side.Ally, unitDB.Families[0], groupLua, unitLua, shipCoordinates, 0, extraSettings);

                if (!groupInfo.HasValue || (groupInfo.Value.UnitNames.Length == 0)) continue; // Couldn't generate group

                mission.Briefing.AddItem(
                    DCSMissionBriefingItemType.Airbase,
                    $"{unitDB.UIDisplayName.Get()}\t-\t{GeneratorTools.FormatRadioFrequency(radioFrequency)}\t{ilsChannel}\t{tacanCallsign}, {tacanChannel}X\t{link4Frequency}");

                unitMaker.CarrierDictionary.Add(flightGroup.Carrier, new CarrierUnitMakerGroupInfo(groupInfo.Value, unitDB.ParkingSpots, template.ContextPlayerCoalition));
                mission.MapData.Add($"CARRIER_{flightGroup.Carrier}", new List<double[]> { groupInfo.Value.Coordinates.ToArray() });
            }
        }

        private static Tuple<Coordinates, Coordinates> GetSpawnAndDestination(
            UnitMaker unitMaker, MissionTemplateRecord template, DBEntryTheater theaterDB,
            List<Coordinates> usedCoordinates, Coordinates landbaseCoordinates, Coordinates objectivesCenter,
            double carrierPathDeg, MissionTemplateFlightGroupRecord flightGroup)
        {
            var travelMinMax = new MinMaxD(Database.Instance.Common.CarrierGroup.CourseLength, Database.Instance.Common.CarrierGroup.CourseLength * 2);
            Coordinates? carrierGroupCoordinates = null;
            Coordinates? destinationPoint = null;
            var iteration = 0;
            var maxDistance = 15;
            var usingHint = template.CarrierHints.ContainsKey(flightGroup.Carrier);
            var location = objectivesCenter;
            if (usingHint)
            {
                location = new Coordinates(template.CarrierHints[flightGroup.Carrier]);
                if (!unitMaker.SpawnPointSelector.CheckInSea(location))
                    throw new BriefingRoomException($"Carrier Hint location is on shore");
            }
            while (iteration < 25)
            {
                carrierGroupCoordinates = usingHint ? location : unitMaker.SpawnPointSelector.GetRandomSpawnPoint(
                    new SpawnPointType[] { SpawnPointType.Sea },
                    landbaseCoordinates,
                    new MinMaxD(10, maxDistance),
                    objectivesCenter,
                    new MinMaxD(10, template.FlightPlanObjectiveDistance.Max/2),
                    template.OptionsMission.Contains("CarrierAllWaters") ? null : GeneratorTools.GetSpawnPointCoalition(template, Side.Ally));
                var minDist = usedCoordinates.Aggregate(99999999.0, (acc, x) => x.GetDistanceFrom(carrierGroupCoordinates.Value) < acc ? x.GetDistanceFrom(carrierGroupCoordinates.Value) : acc);
                if (minDist < Database.Instance.Common.CarrierGroup.ShipSpacing)
                    continue;

                destinationPoint = Coordinates.FromAngleAndDistance(carrierGroupCoordinates.Value, travelMinMax, carrierPathDeg);
                if (unitMaker.SpawnPointSelector.CheckInSea(destinationPoint.Value))
                    break;
                iteration++;
            }

            if (!carrierGroupCoordinates.HasValue)
                throw new BriefingRoomException($"Carrier spawnpoint could not be found.");
            if (!destinationPoint.HasValue)
                throw new BriefingRoomException($"Carrier destination could not be found.");
            if (!unitMaker.SpawnPointSelector.CheckInSea(destinationPoint.Value))
                throw new BriefingRoomException($"Carrier waypoint is on shore");
            if (!ShapeManager.IsLineClear(carrierGroupCoordinates.Value, destinationPoint.Value, theaterDB.WaterExclusionCoordinates))
                throw new BriefingRoomException($"Carrier Route passes though land");

            return new(carrierGroupCoordinates.Value, destinationPoint.Value);
        }

        private static void GenerateFOB(
            UnitMaker unitMaker, ZoneMaker zoneMaker, MissionTemplateFlightGroupRecord flightGroup,
            DCSMission mission, MissionTemplateRecord template, Coordinates landbaseCoordinates, Coordinates objectivesCenter)
        {
            DBEntryTheater theaterDB = Database.Instance.GetEntry<DBEntryTheater>(template.ContextTheater);
            if (theaterDB == null) return; // Theater doesn't exist. Should never happen.

            var usingHint = template.CarrierHints.ContainsKey(flightGroup.Carrier);
            var defaultFlightDistance = landbaseCoordinates.GetDistanceFrom(objectivesCenter);
            var location = Coordinates.Lerp(objectivesCenter, landbaseCoordinates, (defaultFlightDistance > 90 ? 0.3 : 0.5));
            if (usingHint)
                location = new Coordinates(template.CarrierHints[flightGroup.Carrier]);

            Coordinates? spawnPoint = unitMaker.SpawnPointSelector.GetNearestSpawnPoint(new SpawnPointType[] { SpawnPointType.LandLarge }, location);

            if (!spawnPoint.HasValue)
            {
                BriefingRoom.PrintToLog($"No spawn point found for FOB air defense unit groups", LogMessageErrorLevel.Warning);
                return;
            }

            var fobTemplate = Database.Instance.GetEntry<DBEntryTemplate>(flightGroup.Carrier);
            if (fobTemplate == null) return; // Unit doesn't exist or is not a carrier

            double radioFrequency = 127.5 + unitMaker.CarrierDictionary.Count;
            var FOBNames = new List<string>{
                "FOB_London",
                "FOB_Dallas",
                "FOB_Paris",
                "FOB_Moscow",
                "FOB_Berlin"
            };
            var radioFrequencyValue = GeneratorTools.GetRadioFrequency(radioFrequency);
            var groupInfo =
                unitMaker.AddUnitGroupTemplate(
                    fobTemplate, Side.Ally,
                    "Static", "StaticFOB",
                    spawnPoint.Value, 0,
                    new Dictionary<string, object>{
                    {"HeliportCallsignId", FOBNames.IndexOf(flightGroup.Carrier) + 1},
                    {"HeliportModulation", (int)RadioModulation.AM},
                    {"HeliportFrequency", GeneratorTools.FormatRadioFrequency(radioFrequency)},
                    {"RadioBand", (int)RadioModulation.AM},
                    {"RadioFrequency", radioFrequencyValue},
                    {"playerCanDrive", false},
                    {"NoCM", true}});
            if (!groupInfo.HasValue || (groupInfo.Value.UnitNames.Length == 0))
            {
                unitMaker.SpawnPointSelector.RecoverSpawnPoint(spawnPoint.Value);
                return;
            }
            var unitDB = (DBEntryStatic)Database.Instance.GetEntry<DBEntryJSONUnit>(fobTemplate.Units.First().DCSID);
            groupInfo.Value.DCSGroup.Name = unitDB.UIDisplayName.Get();
            groupInfo.Value.DCSGroup.Units.First().Name = unitDB.UIDisplayName.Get();
            zoneMaker.AddCTLDPickupZone(spawnPoint.Value, true);
            mission.Briefing.AddItem(
                     DCSMissionBriefingItemType.Airbase,
                     $"{groupInfo.Value.Name}\t\t{GeneratorTools.FormatRadioFrequency(radioFrequency)}\t\t");
            unitMaker.CarrierDictionary.Add(flightGroup.Carrier, new CarrierUnitMakerGroupInfo(groupInfo.Value, unitDB.ParkingSpots, template.ContextPlayerCoalition));
            mission.MapData.Add($"FOB_{flightGroup.Carrier}", new List<double[]> { groupInfo.Value.Coordinates.ToArray()
});

            foreach (var group in groupInfo.Value.DCSGroups)
            {
                var unit = group.Units.First();
                if (unit.DCSID != "FARP")
                    unit.UnitType = "StaticSupply";
            }
        }
    }
}
