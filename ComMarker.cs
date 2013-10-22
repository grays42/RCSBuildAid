/* Copyright © 2013, Elián Hanisch <lambdae2@gmail.com>
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using UnityEngine;

namespace RCSBuildAid
{
    /* Component for calculate and show forces in CoM */
    public class CoMVectors : MonoBehaviour
    {
        VectorGraphic transVector;
        TorqueGraphic torqueCircle;
        float threshold = 0.05f;
        Vector3 torque = Vector3.zero;
        Vector3 translation = Vector3.zero;

        public float valueTorque {
            get { return torqueCircle.value.magnitude; }
        }

        public float valueTranslation {
            get { return transVector.value.magnitude; }
        }

        public new bool enabled {
            get { return base.enabled; }
            set { 
                base.enabled = value;
                transVector.gameObject.SetActive (value);
                torqueCircle.gameObject.SetActive (value);
            }
        }

        void Awake ()
        {
            /* layer change must be done before adding the Graphic components */
            GameObject obj = new GameObject ("Translation Vector Object");
            obj.layer = gameObject.layer;
            obj.transform.parent = transform;
            obj.transform.localPosition = Vector3.zero;

            transVector = obj.AddComponent<VectorGraphic> ();
            transVector.width = 0.15f;
            transVector.color = Color.green;
            transVector.offset = 0.6f;
            transVector.maxLength = 3f;

            obj = new GameObject ("Torque Circle Object");
            obj.layer = gameObject.layer;
            obj.transform.parent = transform;
            obj.transform.localPosition = Vector3.zero;

            torqueCircle = obj.AddComponent<TorqueGraphic> ();
        }

        void Start ()
        {
            if (RCSBuildAid.Reference == gameObject) {
                /* we should start enabled */
                enabled = true;
            } else {
                enabled = false;
            }
        }

        Vector3 calcTorque (Transform transform, Vector3 force)
        {
            Vector3 lever = transform.position - this.transform.position;
            return Vector3.Cross (lever, force);
        }

        void sumForces<T> (List<PartModule> moduleList) where T : ModuleForces
        {
            foreach (PartModule mod in moduleList) {
                if (mod == null) {
                    continue;
                }
                ModuleForces mf = mod.GetComponent<T> ();
                if (mf == null || !mf.enabled) {
                    continue;
                }
                for (int t = 0; t < mf.vectors.Length; t++) {
                    Vector3 force = mf.vectors [t].value;
                    translation -= force;
                    torque += calcTorque (mf.vectors [t].transform, force);
                }
            }
        }

        void LateUpdate ()
        {
            /* calculate torque, translation and display them */
            torque = Vector3.zero;
            translation = Vector3.zero;

            switch(RCSBuildAid.mode) {
            case DisplayMode.RCS:
                sumForces<RCSForce> (RCSBuildAid.RCSlist);
                if (RCSBuildAid.rcsMode == RCSMode.ROTATION) {
                    /* rotation mode, we want to reduce translation */
                    torqueCircle.valueTarget = RCSBuildAid.Normal * -1;
                    transVector.valueTarget = Vector3.zero;
                } else {
                    /* translation mode, we want to reduce torque */
                    transVector.valueTarget = RCSBuildAid.Normal * -1;
                    torqueCircle.valueTarget = Vector3.zero;
                }
                break;
            case DisplayMode.Engine:
                sumForces<EngineForce> (RCSBuildAid.EngineList);
                torqueCircle.valueTarget = Vector3.zero;
                transVector.valueTarget = Vector3.zero;
                break;
            }

            /* update vectors in CoM */
            torqueCircle.value = torque;
            transVector.value = translation;

            torqueCircle.enabled = (torque.magnitude > threshold) ? true : false;
            transVector.enabled = (translation.magnitude > threshold) ? true : false;

            if (torque != Vector3.zero) {
                torqueCircle.transform.rotation = Quaternion.LookRotation (torque, translation);
            }
        }
    }

    public abstract class MassEditorMarker : EditorMarker_CoM
    {
        protected Vector3 vectorSum;
        protected float totalMass;

        static HashSet<int> nonPhysicsModules = new HashSet<int> {
            "ModuleLandingGear".GetHashCode(),
            "LaunchClamp".GetHashCode(),
        };

        public static float Mass { get; private set; }

        protected override Vector3 UpdatePosition ()
        {
            vectorSum = Vector3.zero;
            totalMass = 0f;

            if (EditorLogic.startPod == null) {
                return Vector3.zero;
            }

            recursePart (EditorLogic.startPod);
            if (EditorLogic.SelectedPart != null) {
                Part part = EditorLogic.SelectedPart;
                if (part.potentialParent != null) {
                    recursePart (part);

                    List<Part>.Enumerator enm = part.symmetryCounterparts.GetEnumerator();
                    while (enm.MoveNext()) {
                        recursePart (enm.Current);
                    }
                }
            }

            Mass = totalMass;
            return vectorSum / totalMass;
        }

        void recursePart (Part part)
        {
            if (physicalSignificance(part)){
                calculateCoM(part);
            }
           
            List<Part>.Enumerator enm = part.children.GetEnumerator();
            while (enm.MoveNext()) {
                recursePart (enm.Current);
            }
        }

        bool physicalSignificance (Part part)
        {
            if (part.physicalSignificance == Part.PhysicalSignificance.FULL) {
                IEnumerator<PartModule> enm = (IEnumerator<PartModule>)part.Modules.GetEnumerator ();
                while (enm.MoveNext()) {
                    PartModule mod = enm.Current;
                    if (nonPhysicsModules.Contains (mod.ClassID)) {
                        return false;
                    }
                }
                return true;
            }
            return false;
        }

        protected abstract void calculateCoM (Part part);
    }

    public class CoM_Marker : MassEditorMarker
    {
        protected override void calculateCoM (Part part)
        {
            float mass = part.mass + part.GetResourceMass ();

            vectorSum += (part.transform.position 
                + part.transform.rotation * part.CoMOffset)
                * mass;
            totalMass += mass;
        }
    }

    public class DCoM_Marker : MassEditorMarker
    {
        static int fuelID = "LiquidFuel".GetHashCode ();
        static int oxiID = "Oxidizer".GetHashCode ();
        static int monoID = "MonoPropellant".GetHashCode ();
        static int solidID = "SolidFuel".GetHashCode ();
        static Dictionary<int, bool> resources = new Dictionary<int, bool> ();

        public static bool other;

        public static bool fuel {
            get { return resources [fuelID]; } 
            set { resources [fuelID] = value; }
        }

        public static bool solid {
            get { return resources [solidID]; } 
            set { resources [solidID] = value; }
        }

        public static bool oxidizer {
            get { return resources [oxiID]; }
            set { resources [oxiID] = value; }
        }

        public static bool monoprop {
            get { return resources [monoID]; }
            set { resources [monoID] = value; }
        }

        void Awake ()
        {
            Load ();
        }

        void Load ()
        {
            DCoM_Marker.other = Settings.GetValue("drycom_other", true);
            DCoM_Marker.fuel = Settings.GetValue("drycom_fuel", false);
            DCoM_Marker.monoprop = Settings.GetValue("drycom_mono", false);
            DCoM_Marker.oxidizer = DCoM_Marker.fuel;
            DCoM_Marker.solid = Settings.GetValue("drycom_solid", false);
        }

        void OnDestroy ()
        {
            Save ();
            Settings.SaveConfig();
        }

        void Save ()
        {
            Settings.SetValue ("drycom_other", DCoM_Marker.other);
            Settings.SetValue ("drycom_fuel", DCoM_Marker.fuel);
            Settings.SetValue ("drycom_solid", DCoM_Marker.solid);
            Settings.SetValue ("drycom_mono", DCoM_Marker.monoprop);
        }

        protected override void calculateCoM (Part part)
        {
            float mass = part.mass;
            IEnumerator<PartResource> enm = (IEnumerator<PartResource>)part.Resources.GetEnumerator();
            while (enm.MoveNext()) {
                PartResource res = enm.Current;
                bool addResource;
                if (resources.TryGetValue (res.info.id, out addResource)) {
                    if (addResource) {
                        mass += (float)res.amount * res.info.density;
                    }
                } else if (other) {
                    mass += (float)res.amount * res.info.density;
                }
            }

            vectorSum += (part.transform.position 
                + part.transform.rotation * part.CoMOffset)
                * mass;
            totalMass += mass;
        }
    }
}