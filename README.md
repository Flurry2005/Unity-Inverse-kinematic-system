# Unity Inverse kinematic system
<img src="https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=csharp&logoColor=white"> <img src="https://img.shields.io/badge/Unity-100000?style=for-the-badge&logo=unity&logoColor=white">

Welcome

Foot and character (Armature) IK for unity.
Applicaple both on players and Nav Mesh Agents.

<h2>Usage</h2>

<p>To use the IK system, the Armature needs to be parented to an empty object during export from the 3D-modeling software. The system is also using <a href="https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.0/manual/index.html">Unity's Animation Rigging Package</a>.</p>

<strong>Player setup:</strong>

<ul style="list-style : number" >
<strong style="text-decoration: underline">Armature</strong>
<li> Add <code>ArmatureIK.cs</code> on the armature object (not empty parent).</li>
<li> Select Character type, <code>Rigidbody</code> and check Using Ridigbody <input type="checkbox" checked style="accent-color:gray">.</li>
<li> Attach rigidbody component.</li>
<li> Attach transforms, Right Foot should be <code>FootRefRF</code>, Left Foot should be <code>FootRefLF</code> and Armature Parent should be the empty parent of the armature.</li>
<li> Adjust Settings, my values: <code>Ray y Offset : 0, Ray Distance : 0.9, Adjust Speed : 5, Update Height : <input type="checkbox" checked style="accent-color:gray"> and Manual Update Toggle : <input type="checkbox" checked style="accent-color:gray"></code></li>
<li> Select layer masks.</li>
</ul>
<ul style="list-style : number" >
<strong style="text-decoration: underline">Feet</strong>
<li> Add <code>FootIK.cs</code> on the Raycast object, child of the FootRig.</li>
<li> Under <strong>Constraints</strong>, attach the multi parent constraint to the <code>Foot Ref Constraint</code> and the Two Bone IK Constraint to the <code>Foot IK</code>.</li>
<li> Under <strong>Transforms</strong>, attach the FootIKTarget.</li>
<li> Adjust Settings, my values: <code>Ray y Offset : 1, Ray Distance : 1.61, Planted Y Offset: 0.35, Exit Ik Threashhold : 0.2, To Ik speed : 150, To No Ik Speed : 150 and IK Foot Rotation : 15</code>.</li>
<li> Select layer masks.</li>
<li>Attach Script References.</li>
</ul>
<ul style="list-style : number" >
<strong style="text-decoration: underline">Player Collider</strong>
<li> Add <code>PlayerColliderUpdater.cs</code> on the object with the collider component.</li>
<li> Under <strong>Player Collider</strong>, attach the collider to <code>Player Collider</code>.</li>
<li> Under <strong>Transforms</strong>, attach the head bone (HeadTop_end) to <code>Head Position</code>, ignore other variables.</li>
<li> Adjust Settings, my values: <code>Ray y Offset : 0, Ray Distance : 7, Addition Head Height: 0 and Additional Center Adjustment : 0</code>.</li>
<li> Select layer masks.</li>
<li>Attach Script References.</li>
</ul>
<ul style="list-style : number" >
<strong style="text-decoration: underline">Player Camera</strong>
<li> Add <code>CameraHeightAdjuster.cs</code> on the object with the Camera component.</li>
<li> Attach the Camera component on <code>Player Camera</code>.</li>
<li> Attach the head bone (Head) to <code>Head bone</code>, and the empty armature parent object to <code>Armature Parent</code>.</li>
<li> Adjust Settings, my values: <code>Adjust speed : 5 and Position offset : x 0; y 0.35; z 0.3</code>.</li>
<li> Select layer masks.</li>
<li>Attach Script References.</li>
</ul>