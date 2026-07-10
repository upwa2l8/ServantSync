# ServantSync — User Guide

**For:** Organization Admins, Ministry Directors, Slot Coordinators, and Volunteers
**Version:** 2026 Edition · Round-FR-5 role hierarchy
**Last updated:** July 2026

---

## How to use this guide

ServantSync is a multi-organization volunteer-scheduling platform. The system has **four distinct user roles**, and the same screen can look and behave very differently depending on which role you hold. This guide is organized so you can read **just the chapter that matches your role** and skip the rest.

| Your role | Read | What you'll do |
|---|---|---|
| **Organization Admin** | [Chapter 5](#chapter-5-the-organization-admin-perspective) | Full org management — members, training, leagues, scheduling |
| **Ministry Director** | [Chapter 6](#chapter-6-the-ministry-director-perspective) | Manage your assigned ministries + their slots |
| **Slot Coordinator** | [Chapter 7](#chapter-7-the-slot-coordinator-perspective) | Manage your assigned slots only |
| **Volunteer** | [Chapter 8](#chapter-8-the-volunteer-perspective) | Sign up for shifts, complete training, view your schedule |

**New to ServantSync?** Start with [Part 1: Getting Started](#part-1-getting-started) (Chapters 1–4), then jump to your role chapter. **Returning user looking for something specific?** Use [Chapter 9: FAQ](#chapter-9-faq) or [Chapter 10: Glossary](#chapter-10-glossary).

> **A note about screenshots.** This guide uses **ASCII wireframes** instead of real screenshots. Real PNG screenshots can be added later — the wireframes here describe each screen's layout, every interactive element, and the typical data flow so you can recognize the screen the moment you see it. The wireframes are sized to roughly match what you'll see in a 1280×800 browser window.

---

## Part 1: Getting Started

### Chapter 1: What is ServantSync?

ServantSync is a volunteer-scheduling platform designed first for churches, then extensible to other scheduling use cases (sports leagues, on-call rotas, conference staffing). The system models three core concepts:

- **Organizations** — the top-level entity (a church, a league, a non-profit). Each org has its own members, ministries, and training catalog.
- **Ministries** — a department or program within an org (Worship Team, Children's Ministry, Springfield Youth Soccer League, etc.). Ministries can have sub-ministries for finer-grained coordination.
- **Service slots** — a specific volunteer role under a ministry (Sunday Sound Tech, Game-Day Referee, Concession Stand Worker, etc.). Slots are what people actually sign up for.

You sign up for a specific **slot** at a specific **time**. The system tracks who is signed up where, prevents double-booking, enforces required training, and gives managers a dashboard view of the next two weeks.

#### The four roles in plain English

Think of the role hierarchy like a four-tier funnel:

| Tier | Role | Scope (what they can see/manage) |
|---|---|---|
| 1 | **SystemAdmin** | All orgs (visibility only; no write bypass) |
| 2 | **Admin** | Full org — every ministry, every slot, every member |
| 3 | **Ministry Director** | Their assigned ministries + sub-ministries transitively |
| 4 | **Slot Coordinator** | Their assigned slots only |
| 5 | **Volunteer** | Their own assignments + open slots they can sign up for |

**A key design principle:** your *membership role* (e.g. "Ministry Director") controls which screens and nav links you can see. Your *entity-level assignment* (e.g. being the `CoordinatorPersonUserId` on the Worship Team ministry) controls which actual data you can see and edit. The two work together — a Ministry Director role without any entity-level assignment sees an empty dashboard, because the system has no idea which ministries they should manage.

### Chapter 2: Logging in for the first time

#### The login screen

```
+----------------------------------------------------------+
|                                                          |
|                                                          |
|                    [ServantSync wordmark]                |
|                                                          |
|                  Sign in to your account                 |
|                                                          |
|        ┌────────────────────────────────────────┐        |
|        │  Email                                  │       |
|        │  ┌──────────────────────────────────┐  │        |
|        │  │ you@example.com                  │  │        |
|        │  └──────────────────────────────────┘  │        |
|        │                                         │        |
|        │  Password                              │        |
|        │  ┌──────────────────────────────────┐  │        |
|        │  │ ••••••••••                       │  │        |
|        │  └──────────────────────────────────┘  │        |
|        │  [ ] Remember me                       │        |
|        │                                         │        |
|        │  [ Sign in  ────────────────────────►  ]        |
|        │                                         │        |
|        │  Forgot your password? · Register      │        |
|        └────────────────────────────────────────┘        |
|                                                          |
+----------------------------------------------------------+
```

#### Steps

1. Open your browser to the URL your administrator gave you (in development, this is typically `http://localhost:5050/Account/Login`).
2. Type your email address into the **Email** field.
3. Type your password into the **Password** field.
4. *(Optional)* Tick **Remember me** if you're on a personal device — this keeps you signed in for 14 days instead of the default session length.
5. Click **Sign in**.

If your email and password match a known account, you'll be redirected to the home page. If they don't, you'll see a red error message above the form: *"Invalid login attempt."* Try again — passwords are case-sensitive.

> **Tip:** If you forgot your password, click **Forgot your password?** below the form. You'll be asked for your email; if the address is registered, a reset link is sent. In development environments, the link is written to the app's console log instead of sent via email — your administrator can copy it from there.

### Chapter 3: The nav menu

After signing in, the left sidebar shows the navigation links you have access to. The links are **role-aware** — different roles see different links. Here's the full map:

| Nav link | Who sees it | What it does |
|---|---|---|
| **Home** | Everyone | Quick links to your most-used screens |
| **Organizations** | Admins only | List of orgs you administer; click into one to manage |
| **Volunteers** | Admins only | Org-wide volunteer directory (the People page) |
| **Leagues** | Admins only | List of leagues (ministries with at least one team) |
| **Dashboard** | Admins, Ministry Directors, Slot Coordinators | Your role-filtered operational view (KPIs + upcoming assignments) |
| **In-person training** | Admins, Ministry Directors | The training catalog (upload, edit, schedule sessions) |
| **Training** | Everyone | Your personal training list — take required training, view your completions |
| **My schedule** | Everyone | Your upcoming assignments + org-wide calendar toggle |
| **Browse open slots** | Everyone | Find and sign up for open shifts |
| **Account** | Everyone | Your name, email, change password |
| **System admin** | SystemAdmins only | Grant/revoke the SystemAdmin identity role |
| **Sign out** | Everyone | End your session |

```
+----------------------+   +----------------------+   +----------------------+
| Admin sees:          |   | Ministry Director:   |   | Volunteer sees:      |
|                      |   |                      |   |                      |
| Home                 |   | Home                 |   | Home                 |
| Organizations  ←  ★  |   |                      |   |                      |
| Volunteers     ←  ★  |   |                      |   |                      |
| Leagues        ←  ★  |   |                      |   |                      |
|                      |   |                      |   |                      |
| Dashboard      ←  ★  |   | Dashboard      ←  ★  |   |                      |
| In-person tr.  ←  ★  |   | In-person tr.  ←  ★  |   |                      |
|                      |   |                      |   |                      |
| Training             |   | Training             |   | Training             |
| My schedule          |   | My schedule          |   | My schedule          |
| Browse open slots    |   | Browse open slots    |   | Browse open slots    |
|                      |   |                      |   |                      |
| Account              |   | Account              |   | Account              |
|                      |   |                      |   |                      |
| Sign out             |   | Sign out             |   | Sign out             |
+----------------------+   +----------------------+   +----------------------+

★ = link is visible to this role only
```

Notice what's missing for each role:
- **Volunteers** don't see any of the management-tier links (Dashboard, Organizations, Volunteers, Leagues, In-person training). Their nav is purely self-service.
- **Slot Coordinators** see Dashboard but no other management links.
- **Ministry Directors** see Dashboard + In-person training but not the org-level management links (Organizations, Volunteers, Leagues).
- **Admins** see everything except System admin.

### Chapter 4: Browser, timezone, and the basics

ServantSync stores all times in **UTC** (Coordinated Universal Time) and converts them to **your browser's local timezone** for display. This means:

- A Sunday service stored as `14:00 UTC` appears as `10:00 AM EDT` to a viewer in New York.
- The same slot appears as `4:00 PM CEST` to a viewer in Berlin.
- Day-bucketing in lists and calendars uses the viewer's local date, not UTC.

The timezone is detected automatically on first page load (via a small JavaScript snippet that sets a cookie). You can see it in effect on any time display: the `LocalTime` component renders a `<time datetime="...">` tag with your local time as the visible text and the UTC value as the machine-readable attribute.

**A common gotcha:** if you travel across timezones and open the app on a new device, you may see slightly different times for the same assignment. The data is correct — your viewer is just in a different zone.

Other browser basics:
- **Modern browser required.** ServantSync uses Blazor Server with SignalR — Chrome 90+, Firefox 88+, Safari 14+, or Edge 90+.
- **JavaScript must be enabled** for the live updates and timezone detection.
- **Cookies must be allowed** for the auth session and timezone cookie.
- **1280×800 or wider** is recommended for the best layout (the layout collapses gracefully to tablet/phone but is optimized for desktop).

---

## Part 2: The Four Role Perspectives

### Chapter 5: The Organization Admin perspective

#### Who you are

You are the **top management tier** for one or more organizations. As an Admin, you can:

- Add, remove, and re-role every member of your organization(s)
- Create and edit ministries, sub-ministries, and service slots
- Upload and manage the training catalog
- See every assignment across the org
- View and manage leagues, teams, and games (if the org runs sports)
- Mark training as complete on behalf of volunteers (in-person sessions)
- See the *DueSoon* grid (who is overdue on which training)

You are the only role that can promote someone to Admin, the only role that can add/remove members, and the only role that sees the *Organizations*, *Volunteers*, and *Leagues* nav links.

#### Your home screen: the Dashboard

```
+------------------------------------------------------------------+
| ServantSync                  Org: [Demo Church ▼]    [Sign out]  |
+------------------------------------------------------------------+
| Home · Dashboard                                               |
|                                                                  |
| KPIs (next 14 days)                                             |
| ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐             |
| │    6     │ │    2     │ │    2     │ │    0     │             |
| │Assignments│ │ Volunteers│ │ Overlaps │ │ Training │             |
| └──────────┘ └──────────┘ └──────────┘ └──────────┘             |
|                                                                  |
| Date range: [Today] [7d] [14d*] [30d] [90d] [All] [Custom]    |
| View:      ( List )  ( Calendar )                                |
|                                                                  |
| ASSIGNMENTS                                                     |
| ── Sunday Jan 12 ──                                              |
| 14:00  Worship / Sunday Sound Tech                              |
|        Vee Volunteer        [Open]                              |
| 14:00  Worship / Sunday Vocals                                  |
|        Val Walker           [Open]                              |
| 14:00  Children's / Sunday School Helper                        |
|        Vee Volunteer        [Open]                              |
|                                                                  |
| ── Sunday Jan 19 ──                                              |
| 14:00  Worship / Sunday Vocals                                  |
|        Vee Volunteer        [Open]                              |
| 14:00  Worship / Sunday Sound Tech                              |
|        Val Walker           [Open]                              |
| 14:30  Worship / Sunday Sound Tech  ⚠️ overlap                   |
|        Vee Volunteer        [Open]                              |
+------------------------------------------------------------------+
```

**What the KPIs mean:**

- **Assignments** — total scheduled assignments in the selected date range, regardless of status.
- **Volunteers** — count of unique people with at least one assignment in the range.
- **Overlaps** — count of assignments that conflict with another assignment for the same person in the same time window. The seed data intentionally has one overlap (Vee's two next-Sunday assignments) so you can see this work.
- **Training** — count of assignments where the assigned person has *not* completed the slot's required training yet. Zero in the seed (everyone is compliant).

The **Date range chips** are presets: Today, 7 days, 14 days (default), 30 days, 90 days, All time, or a Custom range you pick from a date picker. The **List / Calendar** toggle swaps between a list grouped by day (shown above) and a month-grid calendar.

**Overlap rows are highlighted in red.** Click any assignment to open its slot detail page.

#### Common Admin task: promote a member to a different role

The most common Admin task is changing someone's role — e.g. promoting a Volunteer to Ministry Director, or adding a second Admin.

1. Navigate to **Organizations** in the nav.
2. Click the org name to open its detail page.
3. Click the **Members** tab.
4. Find the member in the list. Each row shows name, email, current role (as a colored badge), and a role selector dropdown.
5. Pick the new role from the dropdown next to the member.
6. The change is saved automatically (no "Save" button — it's an instant update).

**Safety guards you should know about:**

- **You cannot demote yourself out of Admin if you are the only Admin.** A second Admin has to do it. This prevents the org from becoming manager-less by accident.
- **You cannot remove the last Admin** of the org. The system will refuse with a clear error message.
- **Adding someone to a ministry/slot as coordinator** is a separate action — see the [Ministry Director chapter](#chapter-6-the-ministry-director-perspective) for that flow.

#### Common Admin task: add a new member who doesn't have an account yet

For someone who doesn't have a ServantSync account (e.g. a new volunteer you met at church):

1. Go to **Organizations → your org → Members** tab.
2. Click **+ Add manually (stub)**.
3. Fill in their name, role, and optional email.
4. The system creates a *stub* Person with a random unusable password and a one-time claim token. You'll see the token on the next page — copy it and hand it to the volunteer (text, print, paper, whatever).
5. When the volunteer visits `/Account/Register?claim=<token>`, they create a real account and the system re-parents all their memberships/assignments/training onto the new account. The stub is gone.

This is a Round-FR-3 flow — designed so volunteers without email access (a common case at smaller churches) can still get set up by handing them a link in person.

#### Common Admin task: view the training-compliance grid (DueSoon)

The DueSoon grid is the org-wide "who needs training" view:

```
+----------------------------------------------------------------------+
| Training due soon — Demo Church                                     |
| Filter: ( All at-risk* )  ( Overdue only )  ( Due in 30d )          |
| Sort:   ( By urgency* )  ( A→Z )  ( By content )                    |
+----------------------------------------------------------------------+
| Person        Content                Status     Due      Action      |
| ------------ ----------------------- -------- ---------- ----------- |
| Vee Vol.      Safe Spaces             Overdue   2 days    [Mark ✓]  |
| Casey Coach   Safe Spaces             Due soon  14 days   [Mark ✓]  |
+----------------------------------------------------------------------+
```

- **Red rows** are *Overdue* (the most recent completion has expired, or there's no completion for a required training).
- **Yellow rows** are *Due soon* (expires within 30 days).
- The default filter is **All at-risk** so you see both groups at once. Switch to **Overdue only** for a more urgent view, or **Due in 30 days** to see who's on the horizon.
- Click **Mark ✓** on a row to mark that person complete on that training (with a required notes field — see Chapter 6 for the in-person session flow).

#### Admin-exclusive nav destinations

**People (the volunteer directory)** — `/People`. A search + filter view of every Person in your orgs, with their role, contact info, and last-activity timestamp. Click a name to open their person-detail page.

**Leagues** — `/Leagues`. Lists every *league* in the org (a league is a ministry with at least one team). Click into a league to see its teams, standings, upcoming games, and recent results.

**Arenas** (sub-tab of an org) — `/Organizations/{id}` with the **Arenas** tab. Lists every playing field / court / surface the org uses. Manage capacity, surface type, and notes here.

### Chapter 6: The Ministry Director perspective

#### Who you are

You are the **mid-tier coordinator** for one or more specific ministries. As a Ministry Director, you can:

- See and manage every slot in your assigned ministries (including sub-ministries transitively)
- See and manage the volunteers signed up to those slots
- Add to the training catalog for the org
- Schedule in-person training sessions and mark attendees complete
- View your ministries' assignments on the Dashboard (scoped to your ministries only)

You are *not* an Admin, so you cannot:
- Add or remove members of the org
- Promote anyone to Admin
- Manage leagues, teams, or games
- See the org-wide volunteer directory

Your authority is driven by **two things together**:
1. Your `OrganizationRole.MinistryDirector` membership label (this controls which nav links you see and the dashboard type)
2. The `Ministry.CoordinatorPersonUserId` field on the specific ministries you're assigned to (this controls which data you actually see)

If you have the role but no `CoordinatorPersonUserId` on any ministry, your dashboard is empty — go to the org's **Manage coordinators** page and ask an Admin to assign you.

#### Your home screen: the Ministry Director Dashboard

The MD dashboard looks similar to the Admin's, but with two important differences:

1. **The org picker only shows orgs where you hold the `MinistryDirector` role.** If you're a Ministry Director in only one org, the picker is hidden and the dashboard shows that org's data.
2. **The assignment list is filtered** to only show assignments in ministries where you are the `CoordinatorPersonUserId`.

```
+------------------------------------------------------------------+
| Ministry director dashboard — Demo Church                       |
+------------------------------------------------------------------+
| KPIs (your ministries, next 14 days)                            |
| ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐             |
| │    5     │ │    2     │ │    2     │ │    0     │             |
| │Assignments│ │ Volunteers│ │ Overlaps │ │ Training │             |
| └──────────┘ └──────────┘ └──────────┘ └──────────┘             |
|                                                                  |
| Date range: [Today] [7d] [14d*] [30d] [90d] [All] [Custom]    |
| View:      ( List )  ( Calendar )                                |
|                                                                  |
| ASSIGNMENTS — Worship Team + Children's Ministry                |
| (matches the same overlap example as the Admin view)             |
+------------------------------------------------------------------+
```

The KPI numbers will be smaller than the Admin's because you're scoped — that's expected and correct.

#### Common MD task: schedule a service slot assignment

The slot schedule is the MD's main workspace.

1. From the Dashboard, click an assignment's **Open** link (or navigate via **Organizations → your org → your ministry → the slot → Schedule**).
2. The schedule page shows a date strip and the slot's existing assignments.
3. Click **+ Assign a volunteer**.
4. Pick the person from the dropdown, set the start time and duration.
5. The system runs three validation checks:
   - **Conflict check** — does this person have another assignment that overlaps in time? If yes, you'll see a red error: *"Conflicts with {their other assignment}."*
   - **Training check** — does this person have the required training for this slot (if any)? If not, you'll see: *"Training required: {content title}."* (And: *"…and currently expired"* if the most-recent completion has expired.)
   - **Slot-level check** — is the slot already full or past? Etc.
6. If all three pass, the assignment is created and you see it in the list.

You can also use **Schedule series** (a button on the schedule page) to create a recurring assignment (e.g. "Vee does Sunday Sound Tech every Sunday for the next 4 weeks"). The system will create one assignment per occurrence and stop if any of them fails a check.

#### Common MD task: upload shared documents to a slot

Slots can have shared documents (welcome packet, run-of-show, training materials, etc.) that every volunteer assigned to that slot can see and download.

1. Navigate to the slot detail page (e.g. `Worship Team → Sunday Sound Tech`).
2. Scroll to the **Shared documents** section at the bottom.
3. Click **Upload a document**.
4. Pick a file (up to 10 MB), fill in a category (e.g. "Setup", "Welcome", "Music") and an optional description.
5. The file is uploaded and listed in the appropriate category group.

You can also **delete** documents you've uploaded (or any document, if you're the slot coordinator). Volunteers can only **download** — they don't see the upload or delete buttons.

#### Common MD task: mark training complete at an in-person session

If you run an in-person training session (e.g. a Safe Spaces refresher during a Sunday school break):

1. Go to **In-person training** in the nav.
2. Click the **Sessions** sub-tab to see existing sessions, or click **+ Schedule a new session**.
3. When the session is happening, open the session's detail page.
4. The **Mark attendees complete** card on the right lists everyone in the org who is assigned to a slot requiring this training but hasn't completed it yet.
5. Tick the checkboxes next to the people who attended, type a short note (required — this is the audit trail), and click **Mark complete**.
6. Each marked person gets a `TrainingCompletion` row with `Source = CoordinatorManual`, the current timestamp, and your note. The next time you look at the DueSoon grid, those rows are gone (or moved to "Compliant").

### Chapter 7: The Slot Coordinator perspective

#### Who you are

You are the **slot-tier coordinator** for one or more specific service slots. As a Slot Coordinator, you can:

- See the assignments on your assigned slots only
- View (but not always edit) the volunteers signed up to your slots
- See the Subscribers count for each of your slots (people who have "subscribed" to be notified of new openings)

You are the most-restricted management tier. You are *not* a Ministry Director, so you cannot:
- Manage the training catalog
- Schedule in-person training sessions
- Manage members
- Add new slots (only Admin and Ministry Director of the parent ministry can)
- See other ministries' data

Your authority is driven by **two things together**:
1. Your `OrganizationRole.SlotCoordinator` membership label (this controls nav visibility and dashboard type)
2. The `ServiceSlot.CoordinatorPersonUserId` field on the specific slots you're assigned to (this controls which data you actually see)

If you have the role but no `CoordinatorPersonUserId` on any slot, your dashboard will show an empty state.

#### Your home screen: the Slot Coordinator Dashboard

The SC dashboard is the most narrowly-scoped of the four. It shows *only* assignments on the slots where you are the `CoordinatorPersonUserId`.

```
+------------------------------------------------------------------+
| Slot coordinator dashboard — Demo Church                         |
+------------------------------------------------------------------+
| You are not yet assigned to any slots.                          |
|                                                                  |
| To get started, ask an Admin or Ministry Director to assign    |
| you as the coordinator of one or more slots via:                |
|                                                                  |
|   Organizations → Demo Church → Coordinators                    |
|                                                                  |
| Or via the per-slot coordinator editor on any ministry's        |
| Roles page.                                                      |
+------------------------------------------------------------------+
```

Once you're assigned to a slot, the dashboard populates with the same KPI/date-chip/list pattern as the Admin and MD dashboards, but scoped to just your slots. You'll also see a *Subscribers (N)* count per slot if any volunteers have subscribed.

#### How you get assigned to slots

You don't assign yourself — an Admin or the parent Ministry Director does it. They go to:

- **Organizations → [org] → Coordinators** (the per-org coordinator management page) — lists every slot across the org with an inline Assign / Edit / Unassign flow. Defaults to the "Unassigned" lens so the slots that need attention bubble to the top.
- **Organizations → [org] → [ministry] → Roles → [slot] → Edit** — the per-slot coordinator editor. Lets the Admin/MD set the coordinator's email, phone, and Person link for one slot at a time.

If you're a new SC and your dashboard is empty, ask the org's Admin to assign you via one of those two paths. Within a minute of being assigned, your dashboard will start showing the slot's data.

### Chapter 8: The Volunteer perspective

#### Who you are

You are the **bottom tier** of the role hierarchy — the largest group of users. As a Volunteer, you can:

- See your own upcoming assignments on **My schedule**
- Browse **open slots** and sign up for them
- Complete required training on the **Training** page
- **Subscribe** to specific slots to be notified when they open
- Toggle between viewing your own schedule and the whole org's schedule (org-wide scope)

You have *no* management authority. You are *not* visible in any nav link that says "manage", and you cannot edit ministries, slots, or members. Your experience is intentionally minimal — find shifts, sign up, show up.

#### Your home screen: My schedule

```
+------------------------------------------------------------------+
| My schedule                                                      |
+------------------------------------------------------------------+
| Scope:  ( My assignments* )  ( All in my orgs )                  |
| View:   ( List* )  ( Calendar )                                  |
| Range:  [Today] [7d] [14d*] [30d] [90d] [All] [Custom]          |
+------------------------------------------------------------------+
| My assignments (next 14 days)                                    |
|                                                                  |
| ── Sunday Jan 12 ──                                              |
| 14:00  Worship / Sunday Sound Tech                              |
|        Sound booth · 120 min                                     |
| 14:00  Worship / Sunday Vocals                                  |
|        Stage · 90 min                                            |
| 14:00  Children's / Sunday School Helper                        |
|        Room 204 · 75 min                                         |
|                                                                  |
| ── Sunday Jan 19 ──                                              |
| 14:00  Worship / Sunday Vocals                                  |
|        Stage · 90 min                                            |
| 14:00  Worship / Sunday Sound Tech                              |
|        Sound booth · 120 min                                     |
| 14:30  Worship / Sunday Sound Tech  ⚠️ overlap                   |
|        Sound booth · 120 min                                     |
+------------------------------------------------------------------+
```

The two red overlap rows on next Sunday are *your* scheduling conflict — both are assigned to you, and the system flags the conflict so you can resolve it (swap with someone, drop one, etc.).

**The Calendar view** is the same data in a month-grid layout, with each assignment as a pill on its day. Click a pill to see slot details. Use the **prev / next / today** navigation to walk between months.

**The All-in-my-orgs toggle** lets you see *other* volunteers' assignments in your orgs too — useful for finding someone to swap with, or just to know what the rest of the team is doing.

#### Common volunteer task: find and sign up for an open slot

1. Click **Browse open slots** in the nav.
2. The page shows an org picker (top), a 3-way filter (All / Subscribed / My ministries), and a date chip strip.
3. Each row is a slot with: time, ministry, slot name, location, and a **Sign up** button.
4. (Optional) Click **Subscribe** on a row to be notified when a new occurrence of that slot opens. Subscriptions persist across time.
5. Click **Sign up** to claim the slot. The system runs the same three checks as the MD's assignment flow (conflict, training, slot-level). If all pass, the assignment is created and you see it in your My Schedule.
6. If any check fails, you'll see a clear error. For training errors, the message names the missing content and a link to take it.

#### Common volunteer task: complete required training

1. Click **Training** in the nav.
2. The page lists every training content your org offers, with your current status:
   - **Compliant** (green) — most recent completion is valid for > 30 days
   - **Due soon** (yellow) — expires within 30 days
   - **Overdue** (red) — expired or no completion on record
   - **Not required** (gray) — this content isn't required for any slot you're assigned to
3. Click **Take training** on a content row.
4. The training view loads — depending on the format, you might see a video player, a slideshow, or a PDF viewer. Watch / read / scroll through to the end.
5. When the content's been fully engaged, the system marks you complete automatically. You'll see the status badge change to **Compliant** within a few seconds.

For *CoordinatorManual* completions (e.g. you attended an in-person session), the system records those automatically when the coordinator marks attendance — you don't need to do anything.

#### Common volunteer task: subscribe to a slot you want to be notified about

Sometimes you want to commit to a slot *when it next opens* without signing up for a specific date (e.g. "any time Sunday Sound Tech comes up, let me know"). The Subscribe feature handles this.

1. Go to the slot's detail page (find it via **Browse open slots** or from a past assignment on your **My schedule**).
2. On the slot detail page, find the **Subscribe** button (top-right or in the slot header).
3. Click it. The button toggles to **Subscribed** and a subscriber count appears.
4. When a new occurrence of this slot becomes available, the system creates a `SlotInterest` row tied to your account, so coordinators can see you as a likely candidate.

You can **Unsubscribe** the same way. The subscription persists across sessions and devices.

---

## Part 3: Reference

### Chapter 9: FAQ

**Q: I can't see the Dashboard link even though I have a role. Why?**
A: The Dashboard is only visible to Admins, Ministry Directors, and Slot Coordinators. Volunteers see My schedule instead. If you think you should have one of those roles, ask an Admin to update your membership via the org's Members tab.

**Q: An overlap row is showing on my schedule but I didn't double-book myself. What gives?**
A: The overlap detection is based on actual time windows — if a coordinator assigned you to two slots whose times overlap (even by a few minutes), both rows are flagged. The dashboard counts *conflicting rows*, not unique people, so a single overlap (Vee's two next-Sunday assignments in the seed) shows up as **2** in the Overlaps KPI.

**Q: I tried to sign up for a slot and got "Training required: Safe Spaces & Child Protection." What do I do?**
A: Click **Training** in the nav, then **Take training** on the Safe Spaces row. Once you complete it (typically a 30–45 minute video), the system marks you compliant within seconds and you can retry the sign-up.

**Q: I tried to sign up for a slot and got "Conflicts with [my other assignment]." What do I do?**
A: You have another assignment in the same time window. The system blocks the overlap to prevent you from being in two places at once. You have three options: (1) drop the other assignment first (ask a coordinator), (2) sign up for a different occurrence of the same slot, or (3) find a swap partner (ask the org if there's a swap UI yet — see Chapter 10).

**Q: As a Ministry Director, my dashboard is empty. Why?**
A: You have the `MinistryDirector` role but no `Ministry.CoordinatorPersonUserId` assignment on any ministry. Ask an Admin to assign you via **Organizations → [org] → Coordinators** (the per-slot coordinator page lists ministries too, in the same flow). Once assigned, your dashboard populates within a minute.

**Q: As a Slot Coordinator, my dashboard is empty. Why?**
A: Same as above — you have the `SlotCoordinator` role but no `ServiceSlot.CoordinatorPersonUserId` assignment. Ask an Admin or the parent Ministry Director to assign you via the org's **Coordinators** page.

**Q: I added a new volunteer (stub) but they never got the claim link. What happened?**
A: In production, the link is emailed. In development, it's written to the app's console log — look for a line like `To: <email> Subject: Claim your ServantSync account …` with the link. Copy the link and hand it to the volunteer (text, in person, whatever). If you lost the token, the Admin can **Rotate claim token** on the Members tab to generate a new one.

**Q: The app's times look off — they're not in my timezone. What's wrong?**
A: ServantSync stores UTC and converts to your browser's timezone on display. If you traveled or are using a new device, the timezone detection runs on first page load and sets a cookie. If it's still wrong, try a hard refresh (Ctrl+Shift+R / Cmd+Shift+R) or sign out and back in. The cookie is domain-scoped and persists.

**Q: I'm an Admin and I can't delete the last Admin of my org — why?**
A: By design. If the only Admin is removed, no one can manage the org anymore. Add a second Admin first, then demote or remove the first.

**Q: What's the difference between "Subscribe" and "Sign up" on a slot?**
A: **Sign up** claims a specific occurrence at a specific time (creates an `Assignment` row). **Subscribe** is a persistent interest flag for the slot — no specific time, just "let me know when this slot opens." Subscriptions are visible to coordinators as a "likely candidate" hint.

**Q: Where can I see the in-app email/notification log?**
A: In development, all outbound emails (password resets, account claims, etc.) are written to the app's console log. In production, they're sent via SMTP (configurable in `appsettings.json`'s `Email` section) and stored by your SMTP provider.

### Chapter 10: Glossary

- **Admin** — Top-tier org manager. Can do anything within their org(s).
- **Assignment** — A scheduled instance of a person filling a specific slot at a specific time. The core data unit of the scheduling system.
- **Arena** — An org-scoped playing surface for sports (Field 1, Gym A, etc.). Shared by all leagues in the org.
- **Claim token** — A one-time-use token that lets a stub Person (someone without an account) claim their pre-seeded memberships/assignments when they register. Issued by Admins when adding a stub.
- **Conflict** — Two assignments for the same person whose time windows overlap. The system prevents creating new conflicts and flags existing ones.
- **CoordinatorPersonUserId** — The Person FK on `Ministry` or `ServiceSlot` that designates the entity-level coordinator. A Ministry Director or Slot Coordinator role label is *necessary* but not *sufficient* — they also need this FK to manage the specific entity.
- **Dashboard** — The role-filtered KPI + assignment view at `/Dashboard`. Three variants (Admin, MD, SC) on the same page.
- **DueSoon** — The org-wide training-compliance grid at `/Organizations/{id}/Training/DueSoon`. Lists every volunteer × every required training with their status.
- **Game** — A scheduled sports event (Home team vs Away team at a specific arena and time). Has its own conflict detection (arena double-book prevention).
- **GameDayCalendar** — The overlay view that shows both games and game-day volunteer shifts on the same day-grouped list.
- **Identity role** — The standard ASP.NET Core Identity role system. The only Identity role ServantSync uses is `SystemAdmin`.
- **League** — A ministry with at least one team. Surfaced separately on the `/Leagues` page.
- **LocalTime** — The Blazor component that converts UTC → browser-local for display. Wraps a `<time datetime="…">` tag for screen readers.
- **Ministry** — A department or program within an org (Worship Team, Children's Ministry, etc.). Can have sub-ministries via `ParentMinistryId` self-FK.
- **Ministry Director** — A user with `OrganizationRole.MinistryDirector` membership. Combined with `Ministry.CoordinatorPersonUserId` assignments, they manage specific ministries.
- **Open slot** — A service slot occurrence that is scheduled in the future and has not been filled (or has open seats). Surfaced on **Browse open slots**.
- **OrganizationRole** — The custom enum ServantSync uses (instead of Identity roles) for org-level access control. Values: `Volunteer = 0`, `MinistryDirector = 1`, `Admin = 2`, `SlotCoordinator = 3`.
- **Person** — The domain entity for a person (one-to-one with an `IdentityUser`). Holds name, email, contact info, and a `IsStub` flag.
- **Slot** — Short for *ServiceSlot*. A specific volunteer role under a ministry.
- **Slot Coordinator** — A user with `OrganizationRole.SlotCoordinator` membership. Combined with `ServiceSlot.CoordinatorPersonUserId` assignments, they manage specific slots.
- **Stub** — A placeholder Person created by an Admin via "Add manually (stub)". Has a random password and a claim token that the real person uses to claim the account on first registration.
- **Subscribe / SlotInterest** — A persistent interest flag linking a volunteer to a slot. Different from an Assignment — no specific time, just "interested in this slot when it opens."
- **SystemAdmin** — The single Identity role used by ServantSync. Grants cross-org visibility only (no write bypass). Managed via the **System admin** nav link.
- **Team** — A group of players (soccer, basketball, etc.) under a ministry. Has a coach, age bracket, and gender.
- **Training content** — A piece of training material (video, slideshow, PDF) that volunteers may be required to complete.
- **Training completion** — A row recording that a person completed a specific version of a specific training content at a specific time. Drives the DueSoon grid.
- **Training requirement** — A rule linking a slot (or the whole org) to a required training content + cadence (Yearly, Every N months, One-time).
- **Volunteer** — The default org role. No management authority; can sign up for shifts and complete training.
- **Wireframe** — A schematic representation of a UI screen. Used throughout this guide instead of real screenshots.

### Chapter 11: Where to get help

**In the app:**
- Click **Account** in the nav to change your password.
- Each page has a header that names the org and the role-filtered scope.
- Date / time displays include a `<time datetime="…">` tag — hover or click for the raw UTC value.

**In the codebase:**
- `README.md` — Quick-start, feature inventory, end-to-end test plan, architecture diagram.
- `STATUS.md` — Working state, known quirks, pending work. **Read this first on every new session.**
- `PLAN.md` — The original product plan with per-feature build status.
- `HANDOFF.md` — Detailed writeups of complex multi-round problems (currently: the SQLite-on-Azure-Files-SMB saga).
- `docs/` — Developer knowledge base. Start with `docs/razor-parser-quirks.md` if you're editing `.razor` files.

**If you find a bug:**
- File an issue in your fork's issue tracker with: (1) the exact steps to reproduce, (2) the role you were signed in as, (3) the URL you were on, (4) any error messages or stack traces from the browser console.

---

## Appendix A: Permissions matrix (cheat sheet)

| Action | Admin | Ministry Director | Slot Coordinator | Volunteer |
|---|:---:|:---:|:---:|:---:|
| View `/Dashboard` (role-filtered) | ✓ | ✓ (their ministries) | ✓ (their slots) | — |
| Add/remove org members | ✓ | — | — | — |
| Change member roles | ✓ | — | — | — |
| Create/edit ministries | ✓ | ✓ (their ministries) | — | — |
| Create/edit slots | ✓ | ✓ (their ministries' slots) | — | — |
| Assign volunteers to slots | ✓ | ✓ (their ministries) | ✓ (their slots, limited) | — |
| Upload training content | ✓ | ✓ | — | — |
| Schedule in-person training | ✓ | ✓ | — | — |
| Mark training complete (in-person) | ✓ | ✓ | — | — |
| View DueSoon grid | ✓ | ✓ (their ministries) | — | — |
| Create games | ✓ (league admin) | — | — | — |
| View own schedule | ✓ | ✓ | ✓ | ✓ |
| View org-wide schedule | ✓ | ✓ (their ministries) | ✓ (their slots) | ✓ |
| Sign up for an open slot | ✓ | ✓ | ✓ | ✓ |
| Complete training (self) | ✓ | ✓ | ✓ | ✓ |
| Subscribe to a slot | ✓ | ✓ | ✓ | ✓ |
| Manage SystemAdmin grants | (SystemAdmin only) | — | — | — |

---

## Appendix B: Quick reference — the 14 demo accounts

If you're running the app in development, these 14 accounts are seeded automatically on first launch. All passwords are `Passw0rd!`.

| Email | Role in Demo Church | Notes |
|---|---|---|
| `admin@demo.local` | Admin | Full org management |
| `coordinator@demo.local` | Ministry Director | Demo MD; manages the Worship + Children's ministries |
| `volunteer@demo.local` | Volunteer | Vee; has 4 assignments across 2 Sundays |
| `volunteer2@demo.local` | Volunteer | Val; 2 assignments across 2 Sundays |
| `league-admin@demo.local` | Admin | League admin for the soccer ministry |
| `coach1@demo.local` | Ministry Director | Casey; coaches U10 Boys |
| `coach2@demo.local` | Ministry Director | Morgan; coaches U12 Girls |
| `ref1@demo.local` | Volunteer | Riley; concussion-compliant |
| `ref2@demo.local` | Volunteer | Quinn; concussion-compliant |
| `devotion@demo.local` | Volunteer | Pre-game devotion leader |
| `concession@demo.local` | Volunteer | Concession stand |
| `parent1@demo.local` | Volunteer | Primary contact for 8 of 16 players |
| `parent2@demo.local` | Volunteer | Primary contact for the other 8 |
| `slotcoordinator@demo.local` | Slot Coordinator | Demo SC; empty state until assigned to a slot |

To **re-seed** (wipe and start over): delete `servantsync.db` (or the `ServantSync` SQL Server database) and restart the app. The seeder is idempotent — it skips when the `Organizations` table has any rows.

---

*End of ServantSync User Guide. Print to PDF for offline reference.*
