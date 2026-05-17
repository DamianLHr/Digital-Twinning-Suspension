# Contributing Guidelines

Welcome to the project! Please read these guidelines carefully before making any contributions. Following these steps ensures a clean, organized workflow for everyone on the team.

---

## Table of Contents

1. [Getting Started](#getting-started)
2. [Creating an Issue](#creating-an-issue)
3. [Working on Your Branch](#working-on-your-branch)
4. [Committing Your Work](#committing-your-work)
5. [Submitting a Merge Request](#submitting-a-merge-request)
6. [General Rules](#general-rules)

---

## Getting Started

Before contributing, make sure you have:

- Access to the GitLab repository
- Cloned the repository locally: `git clone <repository-url>`
- An up-to-date `main` branch: `git pull origin main`

---

## Creating an Issue

Every piece of work **must start with an issue**. Do not start coding without one.

1. Navigate to the repository on GitLab and go to **Issues → New Issue**.
2. Give your issue a clear, descriptive title (e.g., `Add login page UI`).
3. Describe what needs to be done, why it's needed, and any relevant context.
4. Assign the issue to yourself and add appropriate labels (e.g., `feature`, `bug`, `documentation`).
5. Submit the issue — this becomes the basis for your branch and work.

> **Why?** Issues keep the team informed about what everyone is working on and provide a traceable history of decisions and changes.

---

## Working on Your Branch

**You must always work on your own branch. Never commit directly to `main` or any shared branch.**

### Creating a Branch from an Issue

GitLab makes this easy — you can create a branch directly from an issue:

1. Open your issue on GitLab.
2. Click **"Create branch"** (found in the issue sidebar or under the Development section).
3. GitLab will auto-generate a branch name like `Add-login-page-ui`. You may keep this or adjust it, but it should always reference the issue number.
4. Alternatively, create the branch manually and link it to the issue:

```bash
# Make sure you're on an up-to-date main branch first
git checkout main
git pull origin main

# Create and switch to your new branch
git checkout -b Add-login-page-ui
```

### Branch Naming Convention

Use the following format for branch names:

```
(GitLab issue number)-name-of-branch
```

**Examples:**
- `1-Fix-navbar-overlap`
- `2-Add-user-profile-page`
- `3-Update-readme`

---

## Committing Your Work

Write clear, meaningful commit messages so your teammates understand what changed and why.

```bash
git add .
git commit -m "Add responsive navbar with mobile menu"
git push origin Add-login-page-ui
```

**Commit message tips:**
- Use the present tense: *"Add feature"* not *"Added feature"*
- Keep the first line under 72 characters
- Commit often — small, focused commits are easier to review than one giant commit

---

## Submitting a Merge Request

Once your work is complete and tested, you need to open a **Merge Request (MR)** to get your changes reviewed and merged into `main`.

### Steps to Open a Merge Request

1. Push your branch to GitLab (if you haven't already):
   ```bash
   git push origin Add-login-page-ui
   ```
2. Go to the repository on GitLab. You'll likely see a prompt to **"Create merge request"** — click it. Otherwise, go to **Merge Requests → New Merge Request**.
3. Set the **source branch** to your branch and the **target branch** to `main`.
4. Fill in the MR form:
   - **Title:** A clear summary of what the MR does
   - **Description:** Explain what you changed, why, and how to test it
   - **Closes #issue-number:** Include this line to automatically close the linked issue when the MR is merged
5. Assign the MR to yourself and request a review from a teammate or instructor.
6. Submit the MR — do **not** merge it yourself unless you have been told you can.

### Before Submitting, Make Sure You Have:

- [ ] Tested your changes locally
- [ ] Pulled the latest `main` and resolved any conflicts
- [ ] Linked the MR to the relevant issue
- [ ] Written a clear description of your changes

---

## General Rules

- 🌿 **Always branch from an up-to-date `main`**
- 🔗 **Every branch must be linked to an issue**
- 👀 **Every merge requires a Merge Request** — no exceptions
- 💬 **Communicate** — if you're stuck or something changes, update the issue
- 🧹 **Keep your branch focused** — one issue per branch

---

*These guidelines exist to keep the project organized and collaborative. When in doubt, ask a teammate before proceeding.*
