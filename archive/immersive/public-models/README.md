# Modelul buncarului (Blender → glTF)

Pune aici `bunker.glb`; aplicația îl folosește automat (altfel desenează un buncăr placeholder din cod).

Export din Blender: File → Export → glTF 2.0 (.glb), „+Y Up", aplică modificatorii, texturi incluse.
Unități: metri. Scara: om = ~1.7 m, tavan ~3 m. Low-poly e ideal.

## Nume obligatorii / recunoscute (obiecte din scenă)

| Nume            | Ce e                                                                                   |
|-----------------|----------------------------------------------------------------------------------------|
| `Screen_Main`   | Mesh plan cu UV 0..1 pe tot planul (16:9). Pe el apare harta Cesium. Orientat spre cameră. |
| `Terminal_Stats`   | Calculator: statistici + start/pauză/stop/viteză                                    |
| `Terminal_Events`  | Calculator: evenimente (filtre, căutare)                                            |
| `Terminal_Results` | Calculator: rezultate (la pauză / final)                                            |
| `Terminal_Control` | Calculatorul liber: alegi ce urmărește ecranul mare (control direct: etapă viitoare) |
| `Collider_*`    | Mobilier VIZIBIL care blochează mersul (o cutie per mesh-ul din interior).             |
| `Blocker_*`     | Cutie INVIZIBILĂ care blochează mersul (pentru zone pe care nu vrei să le randezi).    |
| `Screen_Secondary` | Ecranul comandantului (orice orientare: perete, inclinat, culcat pe birou). Arată starea simulării; UV-urile se calculează automat. |
| `Button_SpeedUp`, `Button_SpeedDown`, `Button_Pause`, `Button_Reset` | Butoane fizice (tasta E): viteză +/−, pauză/reia, repornește scenariul. Pot fi grupuri cu mai multe mesh-uri. |
| `Spawn`         | Empty: unde apari; privești pe −Z local al empty-ului.                                 |

- Un `Terminal_*` poate fi un grup/empty cu mai multe mesh-uri copil: toate devin interactive (tasta E).
- Limitele camerei = bounding box-ul întregului model, retras cu 0.4 m. Pereții ar trebui să fie deci pereții camerei.
- Dacă modelul nu are lumini, se adaugă o iluminare minimă. Poți pune lumini Blender (se exportă ca KHR_lights_punctual).
- Oamenii/personajele: orice mesh în plus, fără nume special, apare ca decor.
