# CoreCAD V5.0 "Phantom Engine"
### High-Performance Object-Oriented Drafting (OOD) for AutoCAD

CoreCAD V5.0 is a next-generation AutoCAD plugin designed for high-scale architectural and engineering projects. It transitions AutoCAD from a simple drafting tool into a **Data-Centric BIM Engine**, separating Physical Identity (SSOT) from Graphical Representation.

---

## 🚀 Key Features

### 1. The Phantom Engine (Headless Processing)
Process and synchronize hundreds of DWG files in the background without opening them visually. Powered by `ReadDwgFile` technology for 10x - 20x faster updates across entire project sheets.

### 2. Object-Oriented Drafting (OOD)
Implements the **"One Soul, Many Bodies"** philosophy. A single object in the JSON Master can have multiple visual representations (Plan, Section, Detail) across different files, while keeping physical attributes (Width, Material, Marking) perfectly synchronized globally.

### 3. Spatial Scanner (Discovery Engine)
Automatically discovers and registers global project entities into local drawing sheets based on 3D Bounding Boxes. 

### 4. Smart Annotations (Live Labels)
Dynamic labels (`{z}`, `{w}`, `{slope}`) that link directly to physical objects. Change a pipe's elevation in the master database, and every label across 200 files updates instantly.

### 5. BQ Engine (Bill of Quantity)
Automated quantity extraction with **Anti-Double Count** logic. Grouping by GUID ensures that an object appearing across multiple views is only counted once in your material report.

---

## 🛠 Commands
- `CORESYNC`: Push/Pull active drawing to Master JSON.
- `GLOBAL_SYNC`: Batch process all project files (Headless).
- `SCANVISIBLE`: Discover global entities within drawing bounds.
- `SET_VIEW_BOUNDS`: Define the 3D clipping area for the active sheet.
- `EXPORT_BQ`: Generate consolidated material report (CSV).
- `REANNOTATE`: Refresh all smart labels on screen.

---

## 📂 Project Structure
- **/Core**: Geometric reconstruction and slope solver engines.
- **/Models**: OOD Data models (CoreEntity, GeometryData).
- **/Persistence**: JsonEngine, XDataManager, and ProjectContext.
- **/App**: Command registration and AutoCAD entry point.

---

## 📝 Integration
CoreCAD V5.0 targets **.NET 8.0 Windows** (AutoCAD 2025+). It uses `Newtonsoft.Json` for high-speed metadata persistence.

---
**CoreCAD V5.0 - Professional-Grade Architectural Engineering.**
