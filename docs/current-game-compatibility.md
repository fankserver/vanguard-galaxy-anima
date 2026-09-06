# Current compilation reference

The current compatibility target is Vanguard Galaxy **0.8.2.3**, original `Assembly-CSharp.dll` SHA-256 `a2aad60bc68c31baccd636587d3c5ba4e651eacda59b0af42cd4f17f864284fb`.

Run `make refresh-asm` with the owner-installed game and `assembly-publicizer`. This generates a stripped, publicized compile reference in ignored `.local-reference/`. Build/test validate the original assembly hash and generated-reference checksum, then refresh the `lib/` symlink. No game DLL or private receipt is distributed. A game update requires reinspection, not copying an old sibling stub. In an isolated worktree, set `VGMISSIONJOURNAL_DLL` to the current sibling Release DLL path.

Current personnel bindings:
- `Source.Personnel.CommanderSpecialization` replaces the old `Source.Crew` namespace.
- Named officers are `Source.Personnel.OfficerData` in `GamePlayer.officers`; vanilla still uses the `crewMembers` JSON key.
- `AbstractPersonnelData` provides first name, callsign and last name. The prompt retains its existing single-quoted callsign format and `Crew`/`RoleHint` wire fields.
- `Source.Personnel.CrewData` is aggregate ship crew, not the individual officer replacement.

The older decomp/wiki surveys are historical references, not current CLR binding specifications. In particular, old personnel and reward type names must be checked against the installed assembly before reuse. This change proves compilation and preserves formatting; it does not qualify all plugin hooks in-game. The documented five constructor-dependent host-test failures remain separate.
