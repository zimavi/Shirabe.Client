# Shirabe (シラベ)
## Project Definition & Development Brief

**Working name:** Shirabe  
**Japanese spelling:** シラベ  
**Japanese word:** 調べ  
**Meaning:** Tune, melody, musical phrase; also investigation or examination in other contexts.  
**Project type:** Open-source, extensible music platform  
**Primary goal:** Build a music ecosystem that treats music as more than audio files and metadata.

---

# 1. What is Shirabe?

Shirabe is an open-source music platform designed to make listening to, discovering, understanding, organizing, and experiencing music more meaningful.

Most existing music platforms primarily focus on delivering audio, maintaining playlists, and recommending tracks. Shirabe should go further.

Music has context. It has creators, contributors, stories, influences, artistic identities, production techniques, lyrics, cultural references, and relationships with other works.

Shirabe aims to bring these elements together into one coherent experience.

It should combine the convenience and usability of a modern music player with the depth of a music archive, the expressiveness of an artist portfolio, and the flexibility of an open platform.

The platform should not become another engagement-driven social network or simply another Spotify clone.

**Core philosophy: Music deserves more than a play button.**

## 1.1 Product identity

Shirabe should feel:

- **Music-centric:** Every feature should ultimately improve the experience of music.
- **Expressive:** Artists should be able to communicate their identity and the context behind their work.
- **Open:** Users should retain control over their music, metadata, and personal libraries.
- **Extensible:** The platform should support different clients, servers, providers, and integrations.
- **Personal:** Users should be able to customize their listening experience.
- **Respectful:** Artists, contributors, and copyright holders should receive proper attribution and respect.
- **Practical:** Avoid adding complexity without a clear user-facing benefit.

Shirabe should be useful even without access to a centralized streaming catalogue. Local music libraries must be first-class citizens.

---

# 2. Core Features and Differentiators

The following features define Shirabe's intended identity. They are not all required for the initial release.

## 2.1 Dynamic Music Presentation — Flagship Feature

This is one of Shirabe's most important differentiators.

Music presentation should be capable of changing dynamically during playback, synchronized with the actual song timeline.

For example:

- A song begins with one vocalist.
- Another vocalist joins halfway through.
- When that happens, the displayed artist information changes to reflect the additional vocalist.
- A featured artist can become visible at the exact moment they enter the song.
- Artwork, descriptions, credits, and supplementary information can also change at specific timestamps.

This could be implemented as a timeline of presentation events associated with a recording.

Potential event types include:

- Artist or performer visibility changes.
- Display-title changes.
- Artwork changes.
- Description or contextual text changes.
- Credit highlights.
- Instrument or vocalist identification.
- Artist-provided notes.
- Links to relevant annotations.

The experience should be subtle and polished. Transitions might use fades, slides, or other restrained animations.

Important requirements:

1. Presentation must remain synchronized with playback.
2. Seeking must correctly restore the presentation state corresponding to the new timestamp.
3. Events must not accumulate or replay incorrectly after seeking.
4. Metadata and presentation events must be versioned.
5. Full credits and complete recording information must always remain accessible. Dynamic presentation must never hide information from users who want to inspect it.
6. Presentation events should be optional. A recording without them must still work normally.

The goal is to make the listening experience feel alive without making it distracting.

## 2.2 Artist Identity and Profiles

Artist pages should function as a customizable musical identity rather than a generic database entry.

Artists should be able to customize:

- Profile artwork and banners.
- Colors, typography, and visual appearance.
- Backgrounds and layout.
- Biography and external links.
- Featured releases and tracks.
- Discography.
- Collaborators and contributors.
- Musical influences and relationships.
- Additional artist-provided content.

Think of this as a visual and musical identity for an artist, somewhat like Instagram's expressive profiles, but centered entirely around music.

This should not become a social-media engagement system.

## 2.3 Release Prologues and Digital Liner Notes

Artists should be able to provide additional context before or alongside a release.

Examples:

- A message explaining the album.
- The story behind a particular song.
- Inspirations and influences.
- Recording and production details.
- Photographs, sketches, and artwork.
- Videos and behind-the-scenes material.
- Digital liner notes and booklets.

This content should be optional and artist-controlled.

The objective is to give listeners the opportunity to understand the creative context behind music without forcing them to consume additional material.

## 2.4 Rich Credits and Musical Relationships

Shirabe should treat credits as an important part of music metadata.

A recording can have many contributors, each with specific roles:

- Vocals.
- Guitar.
- Bass.
- Drums.
- Keyboards.
- Songwriting.
- Composition.
- Arrangement.
- Production.
- Recording.
- Mixing.
- Mastering.
- Other technical and artistic contributions.

Users should be able to explore these relationships.

For example, opening a guitarist's profile should make it possible to discover recordings they performed on, artists they collaborated with, and projects they participated in.

Credits should be structured data, not just a text field.

## 2.5 Annotations

Shirabe should eventually support community-created annotations.

Unlike lyric-only annotation platforms, annotations should cover music more broadly.

Examples:

- Explaining a lyric or cultural reference.
- Identifying a sample.
- Explaining a particular instrument or production technique.
- Discussing a musical transition.
- Describing the context behind a recording.
- Highlighting relationships with other works.

Annotations could be associated with specific timestamps or sections of a recording.

Artist-verified annotations should be distinguishable from community interpretations.

The platform should preserve attribution and provenance for every contribution.

## 2.6 Lyrics

Lyrics should be an integrated part of the listening experience.

Potential features:

- Synchronized lyrics.
- Multiple translations.
- Original and translated lyrics displayed together.
- Adjustable typography and scrolling.
- Karaoke-style presentation.
- Community corrections and translations.
- Artist-provided annotations.

Lyrics must be handled with copyright in mind.

A community submission does not automatically grant Shirabe permission to redistribute copyrighted lyrics. The platform should track provenance, licensing, permission scope, and applicable restrictions.

External lyric providers may be useful as initial sources, but their terms must permit the intended use.

## 2.7 Personalization and Playback

The player should be highly customizable.

Potential features include:

- Equalizer.
- Playback speed.
- Pitch adjustment.
- Crossfade.
- ReplayGain or loudness normalization.
- Audio visualizers.
- Custom themes.
- Custom player layouts.
- Backgrounds and artwork presentation.
- Output-device selection.
- Gapless playback.
- Offline playback where permitted.

One interesting feature would be a **Nightcore mode**.

This should be implemented as a real-time DSP effect rather than requiring a separate altered audio file. Speed and pitch should ideally be independently configurable, allowing traditional nightcore behavior as well as pitch-preserving speed changes.

Audio quality and format information should be transparent to users.

## 2.8 Discovery

Discovery should help users find music based on actual musical relationships.

Potential approaches:

- Similar artists.
- Shared contributors.
- Musical influences.
- Related recordings.
- Listener overlap.
- Community-curated collections.
- Artist recommendations.

Some personalized recommendation calculations could happen locally on the client.

The platform may collect aggregated, non-personalized information to improve discovery, but privacy must be considered carefully. Aggregation alone does not guarantee anonymity.

---

# 3. Metadata and the Open Data Model

Metadata is fundamental to Shirabe.

The platform should not assume that every recording has one immutable set of metadata.

A useful starting point is to separate metadata into three categories.

### Canonical metadata

Information describing the actual musical work or recording:

- Title.
- Artists.
- Album or release.
- Contributors and credits.
- Release date.
- Recording identifiers.
- Track position.
- Recording relationships.

### Community metadata

Information contributed by the community:

- Annotations.
- Corrections.
- Translations.
- Additional context.
- Identifications and explanations.
- Community-created relationships.

### Personal metadata

Information belonging to an individual user:

- Ratings.
- Personal notes.
- Custom tags.
- Listening preferences.
- Custom metadata fields.
- Personal classifications.

Personal metadata must never be overwritten by canonical or community metadata updates.

## 3.1 Extensibility

Users and instances should be able to introduce custom metadata fields.

Avoid designing a schema that assumes every possible metadata field is known in advance.

A namespaced extension mechanism would allow custom fields while preserving interoperability.

## 3.2 Stable identifiers

Names and titles are mutable. They should not be used as the primary identity of an entity.

Shirabe should distinguish between:

- Artists.
- Compositions.
- Recordings.
- Releases.
- Individual release tracks.
- Contributors.
- Other musical entities.

A composition and a recording of that composition are not the same entity.

Stable identifiers should be used throughout the platform.

## 3.3 Open metadata format

An important long-term objective is an open, documented metadata format that can be used independently of Shirabe's official software.

It should be possible for other applications to read, write, import, and export compatible metadata.

Importing local music should be flexible. Users should be able to map metadata fields from formats such as ID3 and Vorbis comments, and use external identifiers where available.

---

# 4. Local Music and User Libraries

Local music must be a first-class part of Shirabe.

The platform should not require an account, remote server, or streaming subscription to be useful.

The initial client should support:

- Scanning local music directories.
- Reading embedded metadata.
- Editing metadata.
- Managing albums, artists, and tracks.
- Creating playlists.
- Queue management.
- Playback history.
- Personal ratings and tags.
- Artwork management.

Local metadata should be preserved independently of remote metadata.

A remote source must never silently overwrite a user's personal information.

The local library should be designed as a proper data provider, not as a temporary implementation that will later be replaced by the backend.

The architecture should not assume that every track has a local filesystem path.

Use an abstraction such as `IMediaSource` or an equivalent design that can open a media stream from either a local file or a remote provider.

---

# 5. Open Source, Self-Hosting, and Federation

Shirabe should eventually support a federated ecosystem of independent instances.

Users should not be locked into one centralized server.

However, federation is a long-term architectural objective, not an MVP requirement.

## 5.1 General architecture

The system should distinguish between:

**Control plane**

Responsible for network-level coordination:

- Instance identities.
- Trust and authentication.
- Protocol requirements.
- Moderation decisions.
- Rights restrictions.
- Instance admission and revocation.

**Data plane**

Responsible for serving and storing actual content:

- Audio.
- Artwork.
- Metadata.
- Lyrics.
- Search indexes.
- Caches.
- Other content.

An instance operator should be able to choose their database, storage provider, search implementation, and other infrastructure components where the protocol permits it.

## 5.2 Network Authority

The planned federation model includes a Network Authority responsible for publishing signed network-level information.

This includes:

- Policies.
- Moderation decisions.
- Rights restrictions.
- Trusted instance information.
- Protocol requirements.

The exact governance model is not finalized.

The system should remain independently implementable. An instance should not need to run the official Shirabe backend to participate in the ecosystem.

## 5.3 Instance authentication

A deliberately simple credential model is preferred.

- Every trusted instance generates a public/private key pair.
- The private key remains with the instance.
- The Network Authority stores the corresponding public key.
- Instances periodically authenticate by signing a fresh challenge.
- The Authority issues short-lived credentials after successful verification.
- Credentials have a lifetime of approximately 30 minutes.
- Instances renew credentials before expiration.

Removing an instance's registered public key prevents future credential renewal. Already-issued credentials may remain valid until they expire.

A maximum revocation delay of approximately 30 minutes is an accepted trade-off.

Do not introduce additional revocation infrastructure without a compelling reason.

## 5.4 Replication and availability

The architecture should eventually support:

- Replicas.
- Read-only mirrors.
- Failover providers.
- Multiple content sources.
- Distributed storage.

Content-addressed storage and cryptographic hashes may help verify that replicated audio and artwork are identical.

However, technical replication must never be treated as permission to redistribute copyrighted content.

## 5.5 Client-side routing

The client should eventually be able to retrieve different resources from different providers.

For example:

- Audio from one provider.
- Metadata from another.
- Artwork from a third.
- Lyrics from an authorized lyrics provider.

This routing should be transparent to ordinary users.

Advanced users may be able to inspect and override providers.

The client should not need to understand the internal implementation of each provider.

---

# 6. Copyright, Rights, and Moderation

Copyright compliance is a fundamental requirement.

Open-source software does not make copyrighted music, artwork, or lyrics freely redistributable.

Shirabe should initially focus on:

- Local music supplied by users.
- Public-domain music.
- Openly licensed music.
- Independent artists who explicitly authorize distribution.

The long-term goal may include agreements with distributors, labels, publishers, and other rights holders.

Rights must be tracked separately for different types of content, including audio, lyrics, artwork, and supplementary materials.

## 6.1 Moderation philosophy

Shirabe should not host content that promotes propaganda, terrorism, or other dangerous activity.

Discussion, criticism, historical documentation, satire, and fictional depictions of sensitive subjects should not automatically be treated as promotion.

The distinction between discussing a subject and endorsing it is important.

The policy should apply consistently regardless of nationality, language, political movement, or ideology.

In addition to individual content, artist eligibility may be considered separately.

For example, documented support for aggressive war, participation in military propaganda, fundraising for military operations, or promotion of violence against civilians may make an artist ineligible even if a particular song is otherwise harmless.

Moderation should distinguish between:

1. Whether an individual piece of content is permitted.
2. Whether an artist or account is eligible to participate.

Decisions should have documented reasons, evidence, and an appeal mechanism.

Federation should have mandatory network-level safety requirements, while individual instances may implement additional local moderation rules.

---

# 7. Technology and Architecture Direction

The implementation should favor maintainability, modularity, and well-defined boundaries.

The technology choices below reflect current preferences, not irreversible requirements.

## 7.1 Backend

Preferred language: **C#**.

Likely technologies:

- .NET 10.
- ASP.NET Core.
- PostgreSQL.
- Npgsql.
- Entity Framework Core where appropriate.
- S3-compatible object storage.
- FFmpeg for media processing.
- OpenTelemetry for observability.
- xUnit for testing.

Start with a modular monolith.

Potential logical modules:

- Identity.
- Metadata.
- Media.
- Playback-related APIs.
- Library.
- Rights.
- Moderation.
- Federation.
- Discovery.

These should be logical boundaries, not an excuse to create a large collection of independently deployed microservices.

Federation protocols should be language-neutral and independently implementable.

## 7.2 Frontend

The current plan is to use Cider's interface as a starting point and improve upon its design.

Cider should be treated as a UX and visual-design reference, not as something to blindly copy.

Important interface characteristics:

- Sidebar navigation.
- Main content area.
- Persistent playback controls.
- Queue.
- Now Playing view.
- Lyrics.
- Artist and release pages.
- Theme customization.
- Smooth transitions.

The frontend framework has not been finalized.

Avalonia and Tauri are among the candidates. Evaluate them based on actual project requirements rather than personal preference or hype.

## 7.3 Audio engine

The audio engine should be independent of the UI and remote-provider implementation.

It should eventually support:

- Gapless playback.
- Crossfade.
- Equalization.
- Playback speed and pitch.
- Audio output selection.
- Visualization data.
- Lossless formats.
- ReplayGain.
- Local and remote media.

Consider existing audio libraries and native APIs before attempting to implement an audio engine from scratch.

Use an abstraction so that the application is not permanently tied to one playback implementation.

---

# 8. Development Strategy

The development strategy is intentionally local-first.

The preferred order is:

1. Start with the client.
2. Define the API contracts the client will eventually require.
3. Implement playback.
4. Implement a local music library.
5. Build the initial user experience.
6. Only then begin implementing the backend.

This is intended to produce a useful application early and reveal real architectural requirements through actual use.

Avoid implementing federation, distributed infrastructure, and complex backend systems before there is a functional client that needs them.

## 8.1 Initial milestone: Local music player

The first usable milestone should include:

- A polished Cider-inspired interface.
- Local music scanning.
- Metadata reading and editing.
- Album and artist organization.
- Playback.
- Queue management.
- Playlists.
- Basic library management.
- A stable audio engine.

It should work without an account or network connection.

The local implementation should use abstractions that can later support remote providers.

## 8.2 Subsequent milestones

**Milestone 2 — Core platform**

- Backend API.
- Accounts and identity.
- Remote metadata.
- User library synchronization.
- Artist and release pages.
- Structured credits.

**Milestone 3 — Shirabe's identity**

- Dynamic presentation.
- Artist customization.
- Release prologues.
- Digital liner notes.

**Milestone 4 — Community**

- Annotations.
- Lyrics and translations.
- Community metadata contributions.
- Discovery improvements.

**Milestone 5 — Federation**

- Instance authentication.
- Network Authority.
- Signed policies and moderation decisions.
- Multiple content providers.
- Replication and failover.

The exact order can change as implementation reveals dependencies.

---

# 9. Development Guidelines for Claude

These guidelines are important. Follow them throughout the project.

## 9.1 Understand before implementing

Before changing code:

- Inspect the existing project structure.
- Read relevant documentation.
- Understand the current architecture.
- Identify existing abstractions and conventions.
- Check how the affected components interact.

Do not assume that a feature is missing simply because it is not immediately visible.

Do not introduce a new abstraction before understanding whether an existing one already solves the problem.

## 9.2 Favor KISS

Keep solutions as simple as reasonably possible.

Avoid:

- Premature abstraction.
- Unnecessary design patterns.
- Microservices without a real need.
- Excessive configuration.
- Unnecessary dependencies.
- Overly generic frameworks.
- Complex distributed systems before they are required.
- Defensive code that obscures rather than improves correctness.

Do not confuse architectural sophistication with architectural quality.

A small, understandable implementation is preferable to an elaborate system that solves hypothetical future problems.

However, simplicity should not come at the expense of correctness, security, data integrity, or clear architectural boundaries.

## 9.3 Challenge decisions constructively

Do not blindly agree with every proposal.

If an idea has a serious technical drawback, explain it.

When recommending an alternative:

1. Explain the actual problem.
2. Describe the trade-offs.
3. Present a simpler alternative if one exists.
4. Explain why the alternative is preferable.

Avoid inventing problems just to recommend a different architecture.

Respect decisions that have already been made unless there is a concrete reason to revisit them.

## 9.4 Avoid premature implementation

When discussing a new feature, first establish:

- What problem it solves.
- How it fits into the product.
- Its dependencies.
- Its data requirements.
- Its architectural impact.
- Whether it belongs in the current milestone.

Do not immediately create a large implementation for an idea that is still being explored.

For significant architectural decisions, explain the design before making extensive changes.

## 9.5 Maintain clear boundaries

Keep the following concerns separate:

- User interface.
- Playback engine.
- Local library.
- Metadata model.
- Remote content providers.
- Backend API.
- Federation protocol.
- Rights and moderation.

The UI should not need to know which instance or storage provider supplied a track.

The audio engine should not depend on the UI framework.

Local and remote media should be accessed through appropriate abstractions.

Avoid coupling the application to a single backend implementation.

## 9.6 Preserve user data

Never silently discard or overwrite:

- User-created metadata.
- Personal tags.
- Ratings.
- Playlists.
- Library organization.
- Custom fields.

Distinguish canonical metadata from community and personal metadata.

When modifying database schemas, consider migration safety and existing user data.

## 9.7 Code quality

- Write readable, maintainable C#.
- Prefer explicit and understandable code over clever tricks.
- Use asynchronous APIs appropriately.
- Handle cancellation where relevant.
- Avoid unnecessary allocations in performance-sensitive paths.
- Use dependency injection where it genuinely helps.
- Keep interfaces focused.
- Document non-obvious architectural decisions.
- Avoid redundant comments that merely restate the code.
- Keep dependencies to a minimum.

Use tests for important business logic, serialization, metadata transformations, playback behavior, and security-sensitive functionality.

## 9.8 Work incrementally

Prefer small, coherent, reviewable changes.

After implementing a feature:

- Build the affected projects.
- Run relevant tests.
- Fix warnings and errors introduced by the change.
- Verify the intended behavior.
- Update documentation if the architecture or public interfaces changed.

Do not claim that something works without verifying it.

Do not hide incomplete functionality behind misleading success messages.

## 9.9 Be honest about limitations

Clearly distinguish between:

- Implemented functionality.
- Partially implemented functionality.
- Planned functionality.
- Experimental functionality.
- Ideas that have not been validated.

Never present a mock implementation as a working production feature.

If an external dependency has licensing, platform, or compatibility limitations, explain them.

## 9.10 Keep the project's identity in mind

Shirabe is not intended to become a generic streaming service.

When evaluating a feature, consider whether it contributes to one of the following:

- A better listening experience.
- A richer understanding of music.
- Better artist expression.
- Better music organization.
- More meaningful discovery.
- Greater user ownership and openness.

If a feature does not contribute meaningfully to these goals, question whether it belongs.

---

# 10. How Claude Should Collaborate

Act as a technical collaborator and architectural reviewer, not merely a code generator.

The user is interested in understanding the architecture and reasoning behind implementation decisions.

When discussing a proposal:

- Explain the design in concrete terms.
- Identify meaningful trade-offs.
- Be direct about weaknesses.
- Avoid unnecessary complexity.
- Give examples where useful.
- Separate established decisions from suggestions.

When implementing:

- Inspect the repository first.
- Follow its existing conventions.
- Make the smallest complete change.
- Verify the result.
- Summarize what changed and what remains incomplete.

Do not independently expand the scope of a task.

Do not make major product or architectural decisions without discussing them first.

When information is missing, make reasonable assumptions for minor details and state them. Ask questions when a missing decision would materially change the architecture or implementation.

**The goal is to build Shirabe thoughtfully, not to build everything immediately.**

---

# 11. Long-Term Vision

The long-term vision is an open, federated music ecosystem where users can:

- Listen to music from multiple sources.
- Maintain their own libraries.
- Discover artists through meaningful musical relationships.
- Explore rich credits and annotations.
- Experience artist-controlled visual identities.
- Learn about the context behind recordings.
- Customize their listening experience.
- Participate in a community without surrendering ownership of their data.
- Use independent clients and servers.
- Move between providers without being locked into one company.

Artists should have more expressive ways to present their work, and listeners should have more ways to understand and connect with it.

Shirabe should be an ecosystem that can grow beyond its original client and server implementations.

**Shirabe — more than listening. Understanding music.**