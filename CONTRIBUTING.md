# Contributing Guidelines

This document outlines the workflow and practices for contributing to this Unity-based digital twin project for active and predictive suspension systems. Adherence to these guidelines ensures consistent collaboration and prevents the merge conflicts to which Unity projects are particularly susceptible.

---

## Table of Contents

1. [Getting Started](#getting-started)
2. [Creating an Issue](#creating-an-issue)
3. [Working on Your Branch](#working-on-your-branch)
4. [Unity-Specific Workflow](#unity-specific-workflow)
5. [Committing Your Work](#committing-your-work)
6. [Submitting a Pull Request](#submitting-a-pull-request)
7. [General Rules](#general-rules)

---

## Getting Started

Before contributing, ensure you have:

- Access to the GitHub repository
- The Unity version specified in the project README installed (do not upgrade Unity without team agreement, as version changes silently rewrite project files)
- Cloned the repository locally: `git clone <repository-url>`
- An up-to-date `main` branch: `git pull origin main`

---

## Creating an Issue

All work must begin with an issue. Do not start coding without one.

1. Navigate to the repository on GitHub and select **Issues → New Issue**.
2. Provide a clear, descriptive title (e.g., `Add suspension actuator visualization`).
3. Describe the task, its purpose, and any relevant context. Indicate which scenes or prefabs you expect to modify so that team members can identify potential conflicts in advance.
4. Assign the issue to yourself and apply appropriate labels (e.g., `feature`, `bug`, `simulation`, `documentation`).
5. Submit the issue. It will serve as the basis for your branch and as a record of the work.

The issue tracker keeps the team informed of ongoing work and provides traceability for decisions and changes.

---

## Working on Your Branch

All work must be performed on a dedicated branch. Do not commit directly to `main` or any shared branch.

### Creating a Branch from an Issue

GitHub allows branches to be created directly from an issue:

1. Open the issue on GitHub.
2. In the right sidebar under **Development**, select **Create a branch**.
3. GitHub will propose a branch name based on the issue. Retain the issue number in the name.
4. Alternatively, create the branch manually:

```bash
# Ensure main is up to date
git checkout main
git pull origin main

# Create and switch to your new branch
git checkout -b 12-add-actuator-visualization
```

### Branch Naming Convention

Use the following format:

```
(issue number)-short-description
```

Examples:
- `1-fix-damper-coefficient-bug`
- `2-add-prediction-model-prefab`
- `3-update-readme`

---

## Unity-Specific Workflow

Unity projects require additional care compared to typical code repositories. Several Unity file formats produce merge conflicts that are difficult or impossible to resolve manually.

### Scene Merge Conflicts

Unity scenes (`.unity` files) are stored as text-based YAML. While this format is technically diffable, even small edits to a scene can cause Unity to reorder or rewrite substantial portions of the file for internal optimization. If two contributors edit the same scene on separate branches and then attempt to merge, the resulting conflicts are typically not resolvable by hand without corrupting the scene.

**Two contributors must therefore not edit the same scene simultaneously.**

### How to Avoid Scene Conflicts

- **Coordinate before editing a shared scene.** This project has a limited number of scenes, so coordination is essential. Before opening a scene for editing, notify the team via the issue, team chat, or equivalent channel so others know to wait. Inform the team again once you have finished and pushed your changes.
- **Use prefabs whenever possible.** Any reusable element — vehicle components, sensor models, UI panels, simulation managers — should be implemented as a prefab. Prefabs can be edited independently of the scenes that reference them, allowing multiple contributors to work in parallel without conflict.
- **Pull before opening Unity.** If another contributor's changes to a shared scene have been merged into `main`, ensure they are present in your working copy before beginning new edits.

### Project Settings

Changes to files under `ProjectSettings/` affect the entire project and should be made deliberately. If your work requires modifying project settings, state this explicitly in the pull request description so that reviewers can verify the change is intentional.

---

## Committing Your Work

Write clear, descriptive commit messages so that reviewers can understand the intent of each change.

```bash
git add .
git commit -m "Add suspension actuator visualization prefab"
git push origin 12-add-actuator-visualization
```

Commit message guidelines:

- Use the present tense (e.g., *"Add component"*, not *"Added component"*)
- Keep the first line under 72 characters
- Commit frequently in small, focused increments; these are easier to review and revert than large commits
- When modifying a scene or prefab, reference it in the message (e.g., *"Update SuspensionRig prefab: add damping coefficient field"*)

Before committing:

- Save all open scenes before staging changes; unsaved scene state can produce inconsistent commits.
- Review `git status` and the resulting diff. If a scene or prefab shows substantial unexpected changes, investigate before committing.

---

## Submitting a Pull Request

Once your work is complete and tested, open a **Pull Request (PR)** to have your changes reviewed and merged into `main`.

### Steps to Open a Pull Request

1. Push your branch to GitHub:
   ```bash
   git push origin 12-add-actuator-visualization
   ```
2. Navigate to the repository on GitHub. A prompt to **"Compare & pull request"** will typically appear. Otherwise, select **Pull Requests → New Pull Request**.
3. Set the **base branch** to `main` and the **compare branch** to your branch.
4. Complete the PR form:
   - **Title:** a clear summary of the change
   - **Description:** explain what was changed, why, and how to test it. Explicitly list any scenes, prefabs, or project settings that were modified.
   - **Closes #issue-number:** include this line to automatically close the linked issue when the PR is merged.
5. Assign the PR to yourself and request a review from a team member.
6. Submit the PR. Do not merge it yourself unless explicitly authorized to do so.

### Before Submitting, Verify That You Have:

- [ ] Opened the project in Unity and confirmed it compiles without errors
- [ ] Tested your changes in Play Mode where applicable
- [ ] Pulled the latest `main` and resolved any conflicts
- [ ] Linked the PR to the relevant issue
- [ ] Documented the change clearly, including any scenes, prefabs, or project settings modified

---

## General Rules

- Always branch from an up-to-date `main`.
- Every branch must be linked to an issue.
- Every merge requires a pull request.
- Do not edit a shared scene without first coordinating with the team.
- Prefer prefabs over direct scene edits wherever possible.
- All contributors must use the same Unity version; consult the README before upgrading.
- Keep each branch focused on a single issue.
- Communicate blockers or scope changes via the issue.

---

*These guidelines are intended to maintain project integrity and enable effective collaboration. If any aspect of the workflow is unclear, consult a team member before proceeding.*