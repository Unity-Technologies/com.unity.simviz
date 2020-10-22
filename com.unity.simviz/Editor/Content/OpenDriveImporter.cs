using System;
using System.Collections.Generic;
using UnityEditor.Experimental.AssetImporters;
using System.IO;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace UnityEngine.SimViz.Content
{
    [ScriptedImporter(3, "xodr")]
    public class OpenDriveImporter : ScriptedImporter
    {
        public bool validateXmlSchema;

        const string k_OpenDriveSchema = "OpenDRIVE_1.5M.xsd";
        int m_NumValidationErrors;
        HashSet<string> m_ErrorMessages;

        internal static string FindOpenDriveSchema()
        {
            var packagePath = Path.Combine(new [] {"Packages", "com.unity.simviz", "Runtime", "Content"});

            if (!Directory.Exists(packagePath))
            {
                throw new DirectoryNotFoundException("Couldn't find SimViz content directory: " + packagePath);
            }

            var schema = Path.Combine(new[] { packagePath, k_OpenDriveSchema });
            if (!File.Exists(schema))
            {
                throw new FileNotFoundException("OpenDRIVE schema missing from: " + schema);
            }

            return schema;
        }


        static XmlSchemaSet LoadOpenDriveSchema()
        {
            var schemaPath = FindOpenDriveSchema();
            // Debug.Log("Loading OpenDrive schema from " + schemaPath);
            var schemaReader = new StreamReader(schemaPath);
            var schema = new XmlSchemaSet();
            schema.Add("", XmlReader.Create(schemaReader));
            return schema;
        }

        [STAThread]
        public override void OnImportAsset(AssetImportContext context)
        {
            // Debug.Log($"Importing {context.assetPath}...");
            var timeImportStart = Time.realtimeSinceStartup;
            XmlReader openDriveReader;
            if (validateXmlSchema)
            {
                var openDriveSchema = LoadOpenDriveSchema();
                var readerSettings = new XmlReaderSettings
                {
                    ValidationType = ValidationType.Schema,
                    Schemas = openDriveSchema,
                };
                readerSettings.ValidationEventHandler += ValidationCallback;

                openDriveReader = XmlReader.Create(context.assetPath, readerSettings);

                // Debug.Log("Attempting to parse and validate " + context.assetPath);
                m_NumValidationErrors = 0;
                m_ErrorMessages = new HashSet<string>();
            }
            else
            {
                openDriveReader = XmlReader.Create(context.assetPath);
            }

            var openDriveDoc = XDocument.Load(openDriveReader);

            // If the message is empty, no errors were recorded
            if (m_NumValidationErrors == 0)
            {
                // Debug.Log("Successfully parsed and validated file!");
            }
            else
            {
                var consoleMsg = $"Found {m_NumValidationErrors} total errors when validating {context.assetPath}: " +
                    Environment.NewLine + string.Join(Environment.NewLine, m_ErrorMessages);

                Debug.LogWarning(consoleMsg);
            }

            var timeValidateFinish = Time.realtimeSinceStartup;
            // Debug.Log($"XML validation finished - took {timeValidateFinish - timeImportStart} seconds.");

            var factory = new OpenDriveMapElementFactory();
            if (!factory.TryCreateRoadNetworkDescription(openDriveDoc, out var roadNetwork))
            {
                throw new UnityException("Failed to create a valid road network for " + openDriveReader);
            }

            context.AddObjectToAsset("Road Network Description", roadNetwork);
            context.SetMainObject(roadNetwork);
            DontDestroyOnLoad(roadNetwork);

            var timeConstructFinish = Time.realtimeSinceStartup;
            // Debug.Log($"Construction finished - took {timeConstructFinish - timeValidateFinish} seconds");
            // Debug.Log($"Total time to import {context.assetPath}: {timeValidateFinish - timeImportStart} seconds.");
        }

        void ValidationCallback(object sender, ValidationEventArgs eventArgs)
        {
            lock (m_ErrorMessages)
                m_ErrorMessages.Add(eventArgs.Message);
            Interlocked.Increment(ref m_NumValidationErrors);
        }
    }
}
