﻿using System;
using System.IO;
using System.Linq;
using System.Globalization;
using Newtonsoft.Json;
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using Sandwych.MapMatchingKit.Matching;
using System.Collections.Generic;
using Sandwych.MapMatchingKit.Spatial.Geometries;
using Sandwych.MapMatchingKit.Roads;
using Sandwych.MapMatchingKit.Topology;
using Sandwych.MapMatchingKit.Spatial;

namespace Sandwych.MapMatchingKit.Examples.HelloWorldApp
{
    class Program
    {
        private static readonly string s_dataDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "data"));

        static void Main(string[] args)
        {
            var spatial = new GeographySpatialOperation();
            var mapBuilder = new RoadMapBuilder(spatial);

            Console.WriteLine("Loading road map...");
            var roads = ReadRoads(spatial);
            var map = mapBuilder.AddRoads(roads).Build();
            Console.WriteLine("The road map has been loaded");

            var matcher = new Matcher(map, new DijkstraRouter<Road, RoadPoint>(), Costs.TimePriorityCost, spatial);
            matcher.MaxDistance = 1000; // set maximum searching distance between two GPS points to 1000 meters.
            matcher.MaxRadius = 200.0; // sets maximum radius for candidate selection to 200 meters

            var kstate = new MatcherKState();

            Console.WriteLine("Loading GPS samples...");
            var samples = ReadSamples().OrderBy(s => s.Time).ToArray();
            Console.WriteLine("GPS samples loaded. [count={0}]", samples.Length);

            //Do the offline map-matching
            Console.WriteLine("Doing map-matching...");
            var startedOn = DateTime.Now;
            foreach (var sample in samples)
            {
                var vector = matcher.Execute(kstate.Vector(), kstate.Sample, sample);
                kstate.Update(vector, sample);
            }

            Console.WriteLine("Fetching map-matching results...");
            var candidatesSequence = kstate.Sequence();
            var timeElapsed = DateTime.Now - startedOn;
            Console.WriteLine("Map-matching elapsed time: {0}, Speed={1} samples/second", timeElapsed, samples.Length / timeElapsed.TotalSeconds);
            Console.WriteLine("Results: [count={0}]", candidatesSequence.Count());
            foreach (var cand in candidatesSequence)
            {
                var roadId = cand.Point.Edge.RoadInfo.Id; // original road id
                var heading = cand.Point.Edge.Headeing; // heading
                var coord = cand.Point.Coordinate; // GPS position (on the road)
                if (cand.HasTransition)
                {
                    var geom = cand.Transition.Route.ToGeometry(); // path geometry from last matching candidate
                    Console.WriteLine("RoadID={0}\t\tFraction={1}", roadId, cand.Point.Fraction);
                }
            }

            Console.WriteLine("All done!");
            Console.ReadKey();
        }


        private static IEnumerable<MatcherSample> ReadSamples()
        {
            var json = File.ReadAllText(Path.Combine(s_dataDir, @"samples.geojson"));
            var fc = JsonConvert.DeserializeObject<FeatureCollection>(json);
            var timeFormat = "yyyy-MM-dd-HH.mm.ss";
            var samples = new List<MatcherSample>();
            foreach (var i in fc.Features)
            {
                var p = i.Geometry as Point;
                var coord2D = new Coordinate2D(p.Coordinates.Longitude, p.Coordinates.Latitude);
                var timeStr = i.Properties["field_8"].ToString().Substring(0, timeFormat.Length);
                var time = DateTimeOffset.ParseExact(timeStr, timeFormat, CultureInfo.InvariantCulture);
                var longTime = time.ToUnixTimeMilliseconds();
                yield return new MatcherSample(longTime, longTime, coord2D);
            }
        }


        private static IEnumerable<RoadInfo> ReadRoads(ISpatialOperation spatial)
        {
            var json = File.ReadAllText(Path.Combine(s_dataDir, @"osm-kunming-roads-network.geojson"));
            var fc = JsonConvert.DeserializeObject<FeatureCollection>(json);
            foreach (var feature in fc.Features)
            {
                var geom = feature.Geometry as LineString;
                var lineCoords = geom.Coordinates.Select(c => new GeoAPI.Geometries.Coordinate(c.Longitude, c.Latitude)).ToArray();
                var lineGeom = new NetTopologySuite.Geometries.LineString(lineCoords);
                yield return new RoadInfo(
                    Convert.ToInt64(feature.Properties["gid"]),
                    Convert.ToInt64(feature.Properties["source"]),
                    Convert.ToInt64(feature.Properties["target"]),
                    (double)feature.Properties["reverse"] >= 0D ? false : true,
                    (short)0,
                    Convert.ToSingle(feature.Properties["priority"]),
                    120f,
                    120f,
                    Convert.ToSingle(spatial.Length(lineGeom)),
                    lineGeom);
            }
        }
    }
}
