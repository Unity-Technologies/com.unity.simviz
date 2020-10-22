# Converting OpenStreetMap into OpenDRIVE
The following is a suggested approach for converting OpenDRIVE files from OpenStreetMaps. Unity neither provides OpenStreetMaps nor takes any responsibility for the performance of OpenStreetMaps or the tools described here to convert those maps into OpenDRIVE files.

## Getting OSM files from OpenStreet 
1. Go to this [link](https://www.openstreetmap.org/query?lat=69.3439&lon=88.2098#map=5/36.315/-97.559) 
	1. This will open the OpenStreetMap website and allow you to grab sections of road from anywhere 
	2. Each region is submitted by users in that area so beware you may need to do some translation
	3. Select in the top left Export to bring up the export UI
	4. You can do a default square or manually select an area to export 

## OpenStreetMap conversion to OpenDRIVE files is done by using SUMO to access NETCONVERT conversion tool 
Setup:
1. Install the SUMO to use NETCONVERT
2. SUMO (Simulation of Urban MObility) is an open source road traffic simulation tool to handle large road networks
3. NETCONVERT imports digital road networks and generates road networks that can be used by other tools or packages

## Download an OSM file from Open Street Maps
1. Using the Tool
	1. Example cmd for converting osm to xodr
		1. netconvert --osm-files "Map.osm" --opendrive-output "Map.xodr"
