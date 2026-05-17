# Forbidden Words

Inside Layer 2 (the term definition) of `decision-mode`, these words MUST NOT appear unless you have just defined them in the same paragraph.

If you reach for one of these to explain another term, that's a signal: either define this word too, or pick a different angle on the explanation.

This list is project-specific. Add to it when the user points out a term that slipped through; remove from it if the user explicitly says they've internalized the term.

## The current list

### Framework / runtime mechanics
- **middleware** — say "step in the request handling list" instead, OR define middleware first
- **pipeline** — say "ordered list of steps" first
- **primitive** — say "built-in instruction" or "built-in way to" — "primitive" carries a precise meaning the reader has to import
- **predicate** — say "the if-test that decides yes-or-no"
- **handler** — say "the code that responds to a [thing]"
- **wired up** — say "registered" or "connected"; "wired" is hand-wave
- **under the hood** — say what the underlying mechanism actually is; "under the hood" is a promise the explanation never delivers on
- **lifecycle** — say "from when it's created to when it's thrown away"
- **lifetime** (DI sense) — say "how long the same instance is reused"

### Identity / auth
- **principal** — say "the in-memory record of who's logged in for this request"
- **claim** — say "a piece of information attached to the logged-in user (e.g. their email, their roles)"
- **scope** (noun, as in IUserScope) — say "the current-user record this code path is allowed to read"
- **policy** — say "a named yes/no rule (e.g. 'must be admin') that an endpoint can require"

### Data / EF Core
- **DbContext** — say "the object that lets the code read and write the database"
- **query filter** — say "a where-clause that EF Core automatically tacks onto every query for a given entity"
- **change tracker** — say "the in-memory list of pending inserts/updates/deletes EF Core has staged but not saved"
- **optimistic concurrency** — say "the rule where the database refuses your save if someone else changed the row since you read it"
- **migration** — say "a versioned change to the database schema"

### Web / HTTP
- **endpoint** — say "URL path the server answers"
- **route** — say "the rule that maps a URL to the code that handles it" (or define "endpoint" first and use that)
- **CORS** — say "the browser's same-origin rule and the headers servers send to opt out for specific other origins"
- **CSRF** — say "the attack where another site tricks your browser into using your login cookie to call our server"
- **bearer token** — say "a string the client sends in every request to prove it's logged in"

### Process / patterns
- **idempotent** — say "running it twice does the same thing as running it once"
- **eventual consistency** — say "the data will line up across systems, but not immediately"
- **race condition** — say "two operations stepping on each other because they don't know about each other"
- **debounce / throttle** — say "wait until X stops happening before running" / "run at most every N ms"
- **memoize** — say "cache the result of this function so calling it twice with the same input only does the work once"
- **cohort** — say "group of users matching a criterion"
- **fan-out** — say "one trigger that runs the same work for every user / every row"

### "Cute" tech-blog words to avoid

These add nothing — strike on sight.

- **leverage** → use
- **surface** (verb, as in "surface the error") → show
- **bubble up** → return / propagate
- **plumbing** → connection / wiring
- **sweat the details** → say which detail
- **first-class** → built-in / supported
- **opinionated** → has strong defaults
- **idiomatic** — say the specific convention
- **out of the box** → without configuration
- **production-grade** → name what makes it ready (e.g. "has retries and a kill switch")
- **robust** → name the failure mode it survives

## How to add to the list

When the user pushes back on a term:

1. Add the term + suggested replacement here.
2. If the term was structural (i.e. came up in framework explanation), also mention which container it lives in so a future reader knows where to ground it.
3. Don't remove unless the user explicitly says so — terms are sticky for them after correction.
