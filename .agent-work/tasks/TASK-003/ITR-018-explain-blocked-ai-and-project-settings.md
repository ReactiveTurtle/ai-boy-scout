# ITR-018 - Explain blocked AI and place project settings in context

State: verifying (human acceptance pending).

## Finding

Automation was available and enabled. No proposal was processing and no note
was eligible to be claimed. The only queued proposal, for BSR-000149, belongs
to a project without explicit AI read consent. The global 'current proposal'
endpoint instead surfaced an unrelated ready proposal, making the sidebar
status misleading for the selected project.

## Result

The queued note explains why it cannot start and links to settings for its
project. The selected-project sidebar also states that new checks cannot
start without consent. AI consent is not granted automatically. Project AI
access, repository setup, the future storage choice, bindings and transfer
preview live beside the selected project's heading; BoyScout name and
Firebase credentials remain global settings.

## Verification

The live API status was healthy and AI processing enabled, with no eligible
notes. 236 .NET tests and the Angular production build passed. Desktop/mobile
headless screenshots showed the selected-project heading and responsive
settings control. No project consent or storage selection was changed.
