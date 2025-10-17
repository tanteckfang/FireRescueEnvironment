# 🔥 Fire Rescue Simulation Environment

This project is a **human–robot collaboration simulation** that models a fire rescue scenario under **partial observability** and **dynamic conditions**.  
It consists of two main parts:

1. **Python Environment (`fire_rescue_env/`)** – world generation, reasoning, and WebSocket communication.  
2. **Unity Visualization (`FireRescueUnity/`)** – 3D interface for the simulation and robot control.

The simulation supports:
- Dynamic fires and rescues  
- Two robot agents (firefighter + drone)  
- Partial observability (Field of View cones)  
- Human-in-the-loop action selection  
- HAMONY-style structured actions  
- WebSocket bridge between Python and Unity

---

## 🧱 Folder Structure

```
FireRescueSimulation/
│
├── fire_rescue_env/            # Python environment (simulation logic)
│   ├── env/                    # Entity and scene definitions
│   ├── control/                # Rule-based and interactive planners
│   ├── server/                 # WebSocket server (SimpleServer)
│   ├── fire_rescue_env.ipynb   # Example Jupyter Notebook (interactive mode)
│   └── README.md
│
└── FireRescueUnity/            # Unity 3D visualization
    ├── Assets/
    │   └── Scripts/
    │       ├── FireRescueBridge.cs   # Handles WebSocket messages from Python
    │       ├── WorldBuilder.cs       # Builds the scene dynamically
    │       ├── RobotAgent.cs         # Agent movement + interaction logic
    │       ├── Fire.cs               # Fire object component
    │       └── ... (materials, prefabs, etc.)
    └── README.md
```

---

## 🚀 Setup Guide

### 1️⃣ Python Environment

#### Requirements
- Python ≥ 3.10  
- Packages:
  ```bash
  pip install websockets asyncio numpy pyyaml
  ```

#### Run the simulation server
This starts the environment and sends world updates to Unity.

```bash
cd fire_rescue_env_cli_v3
python -m server.websocket_server
```

Or in Jupyter:
```python
from server.websocket_server import SimpleServer
import asyncio

server = SimpleServer('127.0.0.1', 8765)
await server.run_once({'plan_id': 'human-choice', 'steps': steps})
```

The server listens at:
```
ws://127.0.0.1:8765
```

---

### 2️⃣ Unity Environment

#### Unity version
Use **Unity 2022.3 LTS** (recommended).

#### Package dependencies
Install the following via **Window → Package Manager → Add package by name**:

1. `com.endel.nativewebsocket` → WebSocket support  
2. `com.unity.nuget.newtonsoft-json` → JSON parsing  
3. `com.unity.textmeshpro` → UI text rendering

#### Scene setup
1. Open the `FireRescueUnity` project in Unity.  
2. In the **Hierarchy**, create these objects:
   ```
   Main Camera
   Directional Light
   BridgeManager  → Add FireRescueBridge.cs
   WorldBuilder   → Add WorldBuilder.cs
   Canvas         → UI Panel
   EventSystem
   ```
3. In **BridgeManager (Inspector)**:
   - Set `Server URL` → `ws://127.0.0.1:8765`
   - Assign the **WorldBuilder** object to the `WorldBuilder` field.

4. In **WorldBuilder (Inspector)**:
   - Assign prefabs (Robot, Fire, Kit, Human, Obstacle, etc.)
   - Drag the `Canvas` object into the `UiCanvas` field.

#### Play the scene
1. **Run the Python server** first.  
2. Then click **Play** in Unity.  
3. You should see:
   - Robots and fires in a 3D map.  
   - A left-side action panel (`move`, `pick`, `extinguish`, `rescue`, etc.).  
   - Logs in the Unity Console confirming connection.

---

## 🤖 Controls

| Action | Robot | Description |
|--------|--------|-------------|
| `Move (W/A/S/D)` | Both | Move within room boundaries |
| `Extinguish Fire (F)` | Robot1 (blue) | Removes fires in FoV (1 red sphere = 1 fire) |
| `Rescue (R)` | Robot2 (teal) | Carries nearest visible human to safe zone |
| `Pick / Drop` | Both | Manipulate extinguishers or kits |
| `Dynamic World` | - | New fires spawn periodically or after actions |

---

## 🎯 Architecture Overview

| Component | Purpose |
|------------|----------|
| `SceneGraph` (Python) | Represents rooms, objects, and agent state |
| `BeliefMap` | Models partial observability |
| `RulePlanner` | Generates possible actions |
| `SimpleServer` | Sends updates/plans to Unity via WebSocket |
| `FireRescueBridge` (Unity) | Receives JSON world updates & builds UI |
| `WorldBuilder` | Instantiates environment objects dynamically |
| `RobotAgent` | Handles movement, interaction, FoV rendering |

---

## 🔥 Tags & Prefabs

| Shape | Meaning |
|-------|----------|
| 🔵 Blue capsule | `robot1` (legged robot – extinguishes fires) |
| 🟦 Teal capsule | `robot2` (drone – rescues survivors) |
| 🔴 Red sphere | `Fire` (each sphere = 1 fire) |
| 🟣 Magenta cylinder | `Extinguisher` |
| 🟩 Green cube | `First-aid Kit` |
| 🟠 Orange capsule | `Human / Survivor` |
| ◻️ Grey cube | `Obstacle` |
| 🧱 Flat cube | `Room` floor |

---

## 🧠 Partial Observability & Dynamics

- Each robot has an FoV cone (customizable in the Inspector).  
- Objects are **hidden until seen**.  
- New fires may **spawn dynamically** during simulation (`fire_spread_seconds`).  
- Obstacles may shift slightly after each move (simulating structural changes).  

---

## 📡 Communication Protocol

The Python server sends and receives JSON messages:
```json
{
  "plan_id": "human-choice",
  "steps": [
    {"robot": "robot1", "action": "move", "target": "Fire1"},
    {"robot": "robot1", "action": "extinguish_fire"},
    {"robot": "robot2", "action": "rescue_victim"}
  ]
}
```

Unity parses this and updates the 3D world accordingly.

---

## 🧹 Resetting the World
To reset the environment:
- Press **Stop** in Unity.
- Restart the Python notebook or server.
- Press **Play** again.

---

## 🧩 Future Extensions
- Add multi-room communication (door traversal).  
- Implement LLM-based decision planner (Python).  
- Support multi-human rescue scheduling.  

---

## 📄 License
This project is released under the MIT License.
