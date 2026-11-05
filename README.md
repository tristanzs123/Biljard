# Biljard 8-ball (Unity)

3D igra biljarda 8-ball: igralec proti AI nasprotniku s štirimi težavnostmi.
Narejeno na praksi (tehnik računalništva, ERŠ Ptuj).

## Kako zagnati
1. Unity Hub → **Add → Add project from disk** → izberi to mapo.
2. Odpri z **Unity 6000.3.26f1** (URP).
3. Odpri sceno `Assets/_Project/Scenes/Biljard.unity` in pritisni **Play**.
4. Če scena manjka ali je pokvarjena: meni **Biljard → Zgradi sceno**.

## Kontrole
| Akcija | Kontrola |
|---|---|
| Merjenje | premik miške (palica se vrti okoli bele) |
| Natančno merjenje | drži **Shift** |
| Moč | drži **levi gumb** in povleci miško nazaj, spusti = udarec |
| Prekliči udarec | **desni gumb** |
| Bela v roki | premikaj miško, **klik** postavi belo |
| Pogled (poševno / od zgoraj) | **C** |
| Zoom | kolešček |
| Pavza | **Esc** |
| Dodatno merjenje | puščice levo/desno ali **A/D** |
| Učinek (spin) | klik na belo kroglo spodaj desno, desni klik = sredina |

Občutljivost merjenja, natančnega merjenja, puščic in moči, dolžina pomožne črte in glasnost se nastavijo v meniju **Nastavitve** (tudi med pavzo).

## Pravila (WPA, poenostavljeno)
- 1–7 polne, 9–15 črtaste, 8 črna. V trikotniku je 8 v sredini, v zadnjih kotih ena polna in ena črtasta.
- Legalno razbijanje: pade krogla ali vsaj 4 krogle zadenejo blazino.
- Skupina se določi s prvo legalno potopljeno kroglo po razbijanju.
- Prekršek (bela v luknji, bela ne zadene ničesar, prva zadeta ni tvoja, nobena krogla ne zadene blazine, krogla skoči z mize) → nasprotnik dobi belo v roko.
- Črna na koncu = zmaga, črna prezgodaj ali s prekrškom = poraz. Črna na razbijanju = zmaga.

## Arhitektura
```
Assets/_Project/Scripts/
  Rules/     RulesEngine (čist C#, brez MonoBehaviour) + ShotResult/ShotOutcome
  Gameplay/  GameManager (state machine), GameHud, CueStick, CameraViews, Cushion
  AI/        AIPlayer, ShotPlanner (ghost ball + SphereCast), AIDifficultySettings (ScriptableObject)
Assets/_Project/Tests/EditMode/   unit testi za RulesEngine
Assets/Scripts/                   Ball (fizika krogle), CueController (udarec za igralca IN AI), Pocket
Assets/Scripts/Editor/            BiljardSceneBuilder (meni Biljard → Zgradi sceno)
```
- **GameManager** stanja: MainMenu, PlayerAiming, BallsMoving, EvaluatingShot, BallInHand, AITurn, Paused, GameOver.
- Komunikacija prek C# eventov (`Ball.CueBallHit`, `Ball.CushionHit`, `Ball.Pocketed`, `CueController.ShotFired`).
- **AI** uporablja ISTI `CueController` kot igralec: vidno obrne palico, "premisli", potegne nazaj in udari. Za vsako legalno kroglo in vsako luknjo izračuna ghost ball, s `Physics.SphereCast` preveri proste poti, oceni kot reza, razdalje, vstop v luknjo, potrebno moč in (višje težavnosti) pozicijo bele. Če ni dobrega strela, igra obrambno.
- Težavnosti (Lahka, Srednja, Težka, Profesionalna) so v `Assets/_Project/ScriptableObjects/AI`: napaka kota (Gauss), napaka moči, pozicijska igra, safety, verjetnost slabe odločitve.

## Fizika
- Realne mere: miza 9 ft (2.54 × 1.27 m), krogla Ø 57.15 mm, 0.17 kg, ust kotne luknje 12.7 cm, srednje 13.6 cm, koti čeljusti 142° (kotna) in 104° (srednja) po WPA.
- Fixed Timestep 0.005 s (200 Hz): krogla s 8.5 m/s se v enem koraku premakne le ~4 cm, zato so trki natančni.
- Rigidbody: Interpolate, Continuous Dynamic, 12 solver / 8 velocity iteracij.
- Model sukna v `Ball.cs`: drsenje → kotaljenje prek trenja, zato delujejo stun, draw, follow in stranski spin; kotalni upor je lastna sila, pod 0.01 m/s se krogla ustavi.
- Blazine so konveksne prizme s poševnimi čeljustmi ob luknjah, robovi mize imajo okrogle izreze za luknje.

## Unit testi
Window → General → **Test Runner** → EditMode → Run All.

## Tehnologije
Unity 6 (URP), C#, Input System, Unity Test Framework, Git + GitHub.

## Avtor
Tristan (tristanzs123)
