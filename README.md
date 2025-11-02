<h1 style="font-size :32px;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;margin-top:0;">Unity Inverse Kinematic System</h1>

<p style="display: flex; gap:1rem;">
  <img src="https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=csharp&logoColor=white">
  <img src="https://img.shields.io/badge/Unity-100000?style=for-the-badge&logo=unity&logoColor=white">
</p>

<h2 style="font-size :24px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;border-bottom:1px solid #eaecef;padding-bottom:0.3em;">Welcome</h2>

<p style="font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">Foot and character (Armature) IK for Unity.<br>
Applicable both on <strong>players</strong> and <strong>Nav Mesh Agents</strong>.</p>

<h2 style="font-size :24px; border-bottom:1px solid #eaecef;padding-bottom:0.3em;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">Usage</h2>

<p style="font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">To use the IK system, the <strong>Armature</strong> needs to be parented to an empty object during export from the 3D-modeling software.
The system also uses <a href="https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.0/manual/index.html">Unity's Animation Rigging Package</a>.</p>

<h3 style="font-size :20px;margin-top:1.5em; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">Armature</h3>
<ol style="margin-left:1.5em; list-style-type: decimal; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
  <li>Add <code>ArmatureIK.cs</code> on the armature object (not empty parent).</li>
  <li>Select Character type, <code style="background:#656c7633;border-radius:6px;padding:2px 4px;">Rigidbody</code> and check Using Rigidbody <input type="checkbox" checked style="accent-color:gray">.</li>
  <li>Attach rigidbody component.</li>
  <li>Attach transforms: Right Foot → <code>FootRefRF</code>,
      Left Foot → <code>FootRefLF</code> and Armature Parent → the empty parent of the armature.</li>
  <li>Adjust Settings, for example:<br>
    <code>Ray y Offset: 0, Ray Distance: 0.9, Adjust Speed: 5, Update Height: <input type="checkbox" checked style="accent-color:gray">, Manual Update Toggle: <input type="checkbox" checked style="accent-color:gray"></code>
  </li>
  <li>Select layer masks.</li>
</ol>

<h3 style="font-size :20px;margin-top:1.5em;">Feet</h3>
<ol style="margin-left:1.5em; list-style-type: decimal; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
  <li>Add <code>FootIK.cs</code> on the Raycast object (child of the FootRig).</li>
  <li>Under <strong>Constraints</strong>, attach the Multi Parent Constraint to <code>Foot Ref Constraint</code> and the Two Bone IK Constraint to <code>Foot IK</code>.</li>
  <li>Under <strong>Transforms</strong>, attach the FootIKTarget.</li>
  <li>Adjust Settings:<br>
    <code>Ray y Offset: 1, Ray Distance: 1.61, Planted Y Offset: 0.35, Exit IK Threshold: 0.2, To IK Speed: 150, To No IK Speed: 150, IK Foot Rotation: 15</code>
  </li>
  <li>Select layer masks.</li>
  <li>Attach script references.</li>
</ol>

<h3 style="font-size :20px; margin-top:1.5em;">Player Collider</h3>
<ol style="margin-left:1.5em; list-style-type: decimal; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
  <li>Add <code>PlayerColliderUpdater.cs</code> on the object with the collider component.</li>
  <li>Under <strong>Player Collider</strong>, attach the collider to <code>Player Collider</code>.</li>
  <li>Under <strong>Transforms</strong>, attach the head bone (<code>HeadTop_end</code>) to <code>Head Position</code>.</li>
  <li>Ignore other variables.</li>
  <li>Adjust Settings:<br>
    <code>Ray y Offset: 0, Ray Distance: 7, Additional Head Height: 0, Additional Center Adjustment: 0</code>
  </li>
  <li>Select layer masks.</li>
  <li>Attach script references.</li>
</ol>

<h3 style="font-size :20px; margin-top:1.5em;">Player Camera</h3>
<ol style="margin-left:1.5em; list-style-type: decimal; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
  <li>Add <code>CameraHeightAdjuster.cs</code> on the object with the Camera component.</li>
  <li>Attach the Camera component to <code>Player Camera</code>.</li>
  <li>Attach the head bone (<code>Head</code>) to <code>Head Bone</code> and the empty armature parent to <code style="background:#656c7633;border-radius:6px;padding:2px 4px;">Armature Parent</code>.</li>
  <li>Adjust Settings:<br>
    <code>Adjust Speed: 5, Position Offset: x 0; y 0.35; z 0.3</code>
  </li>
  <li>Select layer masks.</li>
  <li>Attach script references.</li>
</ol>
