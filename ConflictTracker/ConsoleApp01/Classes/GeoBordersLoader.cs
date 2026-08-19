using ConflictConsole.Interfaces;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using System;
using System.Collections.Generic;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;

namespace ConflictConsole.Classes
{
    internal class GeoBordersLoader : IBorderLoader
    {

        public Task<CoordinateDTO[]> LoadBordersAsync(string[] args)
        {


            string shapefilePath = args[0];
            string country = ExtractCountryFromShapefilePath(shapefilePath);
            ShapefileDataReader reader = null;
            if (string.IsNullOrEmpty(country))
            {
                throw new ArgumentException($"Specified country {country} is null or empty.");
            }
            else if (string.IsNullOrEmpty(shapefilePath))
            {
                throw new ArgumentException($"Specified shapefile {shapefilePath} is null or empty.");
            }
            else if (!System.IO.File.Exists(shapefilePath) && !Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains(shapefilePath))
            {
                throw new ArgumentException($"Specified shapefile {shapefilePath} does not exist.");
            }
            else if (!System.IO.Path.GetExtension(shapefilePath.Trim()).Equals(".SHP", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Specified shapefile {shapefilePath} is not a .shp file.");
            }
            else
            {
                //create the reader for the shapefile

                reader = CreateReader(shapefilePath);



                var coords = new List<CoordinateDTO>();

                while (reader.Read())
                {
                    var geom = reader.Geometry;

                    foreach (var c in geom.Coordinates)
                    {
                        coords.Add(new CoordinateDTO { Latitude = c.Y, Longitude = c.X, Country = country });
                    }
                }

                return Task.FromResult(coords.ToArray());
            }
        }

        /// <summary>
        /// Since the shapefile path is expected to contain the country name, this method extracts the country name from the path.
        /// </summary>
        /// <param name="shapefilePath"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        private string ExtractCountryFromShapefilePath(string shapefilePath)
        {

            string[] countries = { "Egypt"
                                ,"Libya"
                                ,"Chad"
                                ,"Sudan"
                                ,"South_Sudan"
                                ,"Kenya"
                                ,"Uganda"
                                ,"Ethiopia"
                                ,"Somalia"
                                ,"Central_African_Republic" };

            string[] components = shapefilePath.Split(".");
            for (int i = 0; i < components.Length; i++)
            {
                if (countries.Any(country => components[i].Equals(country, StringComparison.OrdinalIgnoreCase)))
                {
                    //Return the proper country name, _ only appears for resource paths. We need country to match Place.Country (i.e. with spaces) as already in the KG.
                    return components[i].Replace("_", " ");
                }
            }
            throw new ArgumentException("Country not found in shapefile path ({shapefilePath}).");


        }

        /// <summary>
        /// return a shapefilereader for the specified shapefile path. If the path is not a valid full path, it will attempt to find the file in the output directory.
        /// </summary>
        /// <param name="shapefilePath"></param>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException"></exception>
        private static ShapefileDataReader CreateReader(string shapefilePath)
        {

            // Enable legacy encodings like 1252
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);


            // Case 1: shapefilePath is already a real file path
            if (File.Exists(shapefilePath))
                return new ShapefileDataReader(shapefilePath, GeometryFactory.Default);

            // Case 2: shapefilePath is an embedded resource base name (e.g., "borders.shp")
            var assembly = Assembly.GetExecutingAssembly();
            var resourceNames = assembly.GetManifestResourceNames();

            // Find all matching resources: .shp, .shx, .dbf, .prj
            string[] requiredExt = { ".shp", ".shx", ".dbf", ".prj" };

            string validResourcePath = Path.GetFileNameWithoutExtension(shapefilePath);

            var matchedResources =
                requiredExt
                .Select(ext => resourceNames.FirstOrDefault(r => r.EndsWith(validResourcePath + ext, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            // Ensure all required resources exist
            if (matchedResources.Any(r => r == null))
                throw new FileNotFoundException($"Embedded shapefile resources missing for base name: {shapefilePath}");

            // Extract all 4 files to a temp directory
            string tempDir = Path.Combine(Path.GetTempPath(), "shp_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            foreach (var resName in matchedResources)
            {
                int length = resName.Split(".").Length;

                string extension = resName.Split(".").Last();

                string fileName = resName.Split('.').ElementAt(length - 2); // extract actual file name
                string outPath = Path.Combine(tempDir, fileName + "." + extension);

                using var resStream = assembly.GetManifestResourceStream(resName);
                using var outStream = File.Create(outPath);
                resStream.CopyTo(outStream);
            }

            // Return reader for the extracted .shp file
            string shpPath = Directory.GetFiles(tempDir, "*.shp").First();
 
            return new ShapefileDataReader(shpPath, GeometryFactory.Default);

        }
    }
}
