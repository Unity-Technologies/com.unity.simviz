# Road Meshing

## Creating A Road Path Network

To create a __Road Path Network__ GameObject, select the menu item GameObject > Simviz > Road Path Network.


## Editing Road Paths

To start designed your new __Road Path Network__, navigate to your __Road Path Network__ GameObject created in the heirarchy. Add a new __Road Path__ by clicking the __Add Road Path__ button or edit existing __Road Paths__ by opening the _Road Paths_ drop down and double clicking on one of the listed __Road Path__ GameObjects to navigate to them in the hierarchy.

Once a __Road Path__ GameObject is selected, you will see red spline points appear in the Scene view. A green path will form between these control points representing the center line a new road within your road network. Move spline points by clicking and dragging their transform GUI in the scene view. Add points by either pressing shift and clicking somewhere in the scene or by ctrl-shift clicking anywhere on the green path between two spline points to add a new intermediate point. Use ctrl-click to remove an existing spline point.

More granular control over the shape of the resulting road spline can be obtained by changing the __Road Path__'s _Control Mode_ to an option other than Automatic. Blue control points should appear, one for each red spline point. Moving a blue control point will change the direction and weight of the spline curvature through a particular control point.

More information about manipulating road paths can be found in this [user guide](https://docs.google.com/document/d/1-FInNfD2GC-fVXO6KyeTSp9OSKst5AzLxDaBRb69b-Y/edit).


## Road Mesh Configuration

The __Road Path Network To Mesh__ script has a variety of parameters for configuring generate road meshes. A selection of these parameters are explained in detail below:

| Parameter | Function |
| --------- | ------ |
| Base Random Seed | A random number used to salt every random value generated while creating a road mesh. |
| Decal Density | Controls the number of decals generated per meter across a road mesh's surface |
| Generate Corner Debug Lines | A useful debug option for visualizing the outlines of the road profiles and rounded corner tangents used to shape the border of generated intersection meshes |
| Num Intersection Samples | Specifies the resolution at which to sample the intersection mesh |
| Random Num Lanes | An option for randomizing the number of lanes assigned to each road segment |
| Real Time | Recreates the road mesh once for each new frame in the editor. Useful for visualizing the effect of changing the base random seed parameter. |
| Unique GameObject Per Material | Divides a generated road mesh by material and places each sub mesh into a separate GameObject |
