# Deliverance vNext archaeology and version audit

## Repository archaeology

The audit covered `Deliverance.Core` built-ins, codecs, binary format, segmented IO, modules, MessagePack serialization, filesystem/cloud-shaped storage interfaces, `DeliveranceService`, `IDeliverance`, options, Stride registration, all tests, the v1 golden fixture, and the complete commit history from the initial cloud-shaped store through M2/M3 migration and M4 inspection.

| Concern | V1 behavior | Abandoned V2 behavior | Current failure before vNext | VNext law |
| --- | --- | --- | --- | --- |
| container identity | `DLVR`, integer, timestamp, build, directory | added four byte IDs and variable metadata | reader interpreted every integer with one layout; partial work required v2 while options still emitted v1 | `DLVR` dispatches explicitly: v1 importer or v2 reader; writer emits v2 only |
| version meaning | `ContainerVersion` was configurable but not enforced | structural v2 was mixed with module work | a single caller knob could claim arbitrary layouts | format version is a library constant; domain changes never bump it |
| module version | `ISaveModule.Version` | same plus more directory fields | mutable live modules restored themselves; newer saves could enter an invalid migration path | positive module schema version; module owner supplies consecutive forward byte migrations |
| application compatibility | only `BuildId` | no coherent addition | build identity was provenance and de facto compatibility | separate application ID/version/save-version, build, definition hash, cadence hash |
| missing/incompatible module | global ignore/warn/error | unchanged | silent skip could leave partially mutated live state | required fails; optional warns and skips; no commit occurs inside Deliverance |
| serialization | one global MessagePack serializer | serializer ID placeholder always zero | metadata could not actually resolve historical serializers | payload records carry serializer ID; raw/custom codecs use reserved ID zero |
| compression | none/gzip registry | renamed codec to compression | reasonable seam, but old files depended on current registry | per-module compression ID is persisted and resolved |
| encryption | none | IDs and metadata placeholders | no implementation, authentication, key law, or tests | optional AES-256-GCM; caller key provider; random nonce; authenticated metadata |
| integrity | none | hash placeholders | payload corruption could become DTO garbage | SHA-256 semantic-payload check; AEAD authentication when encrypted |
| storage | byte and streaming interfaces | unchanged | `.dlv_b`, incomplete traversal sanitization, shared `.tmp`, backup rotation before durable temp | `.dlv`, safe derived name, unique temp, flush-to-disk, atomic replace, bounded backups, per-path lock |
| load boundary | `Restore` mutated registered services | unchanged | later module failure could leave earlier application state mutated | `LoadAsync` returns an immutable candidate; application validates and commits |
| discovery | explicit `Register` | proposed ID registries | mutable registry coupled save infrastructure to live objects | explicit `SaveRequest` payload list and explicit load definitions; no scan, attributes, or static registry |
| Stride | thin service registry adapter | unchanged | old README/product shape implicitly centered Stride | retained as optional leaf; Core has no Stride/Dominatus/Aurelian dependency |

## V1/V2 autopsy

V1's integer looked like a format discriminator but the reader never branched or rejected it. It always parsed the v1 directory: module key, module version, one compression byte, offset, and length. Consequently changing the integer alone did not define compatibility.

The abandoned working tree attempted a structural v2 directory with serializer/compression/encryption/hash IDs and metadata. It simultaneously left `DeliveranceOptions.ContainerVersion` at 1, made the writer require 2, made the reader v2-only, and deleted the sole v1 fixture. Ordinary saves therefore failed before writing and legacy behavior lost its executable evidence. There was no committed v2 fixture or released v2 contract to preserve.

VNext resolves this without compatibility theater: v2 is the one new format; v1 is a narrow read-only importer proven by the real `golden_v1.dlv_b`; all other versions fail with `UnsupportedContainerVersion`.

## Final laws

- `ContainerFormatVersion = 2` belongs to Deliverance's binary layout only.
- `ModuleSchemaVersion` belongs to each module. Old schemas advance one explicit step at a time. Missing paths fail. Newer required schemas fail; newer optional schemas warn and skip.
- `ApplicationSaveVersion` is an optional application composition contract. `ApplicationVersion` and `BuildId` are release provenance.
- `DefinitionHash` is a hard compatibility check when supplied. TinyFarm treats mismatch as failure.
- `CadenceConfigHash` is a hard check for deterministic TinyFarm save/replay because cadence controls semantic time partitioning.
- Required unknown/missing modules fail before application commit. Optional unknown/missing modules are skipped with diagnostics.
- Unencrypted output is byte-identical for equal explicit bytes and equal provenance. `CreatedUtcUnixSeconds` defaults to zero so time is never hidden nondeterminism. Applications may deliberately supply a timestamp.
- Encrypted ciphertext is intentionally nondeterministic; SHA-256 over semantic module bytes remains stable.
- Plaintext metadata includes format and codec/encryption identifiers, nonce/tag, and compatibility facts. Payload content is encrypted. AES-GCM associated data binds module ID, schema, serializer, compression, and semantic hash.
- Snapshot capture is synchronous and application-owned. Serialization and IO may run off-thread only after capture. Candidate commit returns to the application thread.

## Package decision

- `Deliverance.Core`: standalone container, codecs, modules, migrations, encryption, diagnostics, slots/storage.
- `Deliverance.Dominatus`: thin persistence-actuation adapter; depends on Core and Dominatus.
- `Deliverance.StrideConn`: retained as a small optional legacy connector because it remains a valid leaf and builds; it has no authority over Core.

No extra JSON/encryption/storage packages were created: the boundaries are real but still compact.
