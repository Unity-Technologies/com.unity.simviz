# Installing the SimViz package in your project

![ReleaseBadge](https://badge-proxy.cds.internal.unity3d.com/5ab9a162-9dd0-4ba1-ba41-cf25378a927a)

This page provides brief instructions on installing the SimViz package. Once done check out the ways to create road networks listed down below:

[Creating a Open Drive Network](OpenDriveNetwork.md)

[Creating a Spine Based Road Network](SpineRoadNetwork.md)

1. Install the latest version of 2019.4.x Unity Editor from [here](https://unity3d.com/get-unity/download/archive). (SimViz has not been tested on Unity versions newer than 2019.4)
1. Create a new HDRP or URP project, or open an existing project.
1. Open `Window` ->  `Package Manager`
	1. In the Package Manager window find and click the ***+*** button in the upper lefthand corner of the window
	1. Select ***Add package from git URL...***
	1. Enter `com.unity.simviz` and click ***Add***

If you want a specific version of the package, append the version to the end of the "git URL". Ex. `com.unity.simviz@0.1.0-preview.5`

To install from a local clone of the repository, see [installing a local package](https://docs.unity3d.com/Manual/upm-ui-local.html) in the Unity manual.

## Open Street Map conversion to OpenDRIVE files

### Setup:
* Install the [SUMO](https://sumo.dlr.de/releases/1.3.1/sumo-win64-1.3.1.msi) to use NETCONVERT
* Download an osm file from [Open Street Maps](https://www.openstreetmap.org/export#map=13/47.5980/-122.1551)
 * Or grab a map from [simViz](https://drive.google.com/drive/u/0/folders/1-nbFgyn-lFqzLqMz6UpzINIsowZUx10t) team 

### Using the Tool
* Example cmd for converting osm to xodr
* netconvert --osm-files "Map.osm" --opendrive-output "Map.xodr"