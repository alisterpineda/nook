# Accounts

Nook has its own accounts, so a household can use it with no identity provider. Each person has an email, a
password and a role. An admin adds people, gives them passwords, changes their roles and deletes them on the
"Users" page under "Manage". Everyone else changes their own password on the Account page, which the user menu
opens. [Single sign-on](single-sign-on.md) is an addition to this, not a requirement.

The first account is made on the setup page the first time Nook is opened, and is an admin.

## Roles

| Role | What it may do |
|---|---|
| Admin | Everything a contributor may, and the pages under "Manage": users, public access, jobs and the audit log. |
| Contributor | Browse, read and download everything, and upload books. |
| Viewer | Browse, read and download everything. |

An admin can give only these three. Nook has a fourth role, Anonymous, which is what a visitor with no account
gets under [public access](public-access.md), and a person cannot hold it.

To change someone's role, open the actions menu on their row, choose "Change role", pick the role and save. The
person's session carries the new role within 5 minutes, when Nook next checks it.

## Adding people

Choose "Add user" on the Users page and give an email, a role, an initial password and, if you like, a name.
Without a name, Nook shows the part of the email before the `@`. The person can then sign in with that email
and password, and the new account shows in the list. An email that already has an account, or a password the
rules below refuse, shows a message on its field and creates nothing.

Nook sends no email. Tell the person their email and the password you chose, and ask them to change the
password on their Account page.

## Passwords

A password is at least 12 characters, with no other rule. Five wrong passwords in a row lock the account for 5
minutes. The lock ends by itself, and setting a new password does not end it.

### Changing your own

Open "Account" in the user menu. The Account page shows your email, name and role, and a form for the password:
give the current password, then the new one twice. A wrong current password, or a new one the rules refuse,
shows a message on that field and changes nothing. Afterwards the new password signs in and the old one does
not. The session that made the change stays signed in. Your other sessions are signed out within 5 minutes.

### Setting someone else's

Someone who has forgotten their password gets a new one from an admin: open the actions menu on their row,
choose "Set password" and type one. Tell them the password, and ask them to change it on their Account page.
Their old password stops working, and their sessions are signed out within 5 minutes. It also works for a
person who has no password, such as one who joined through single sign-on, and gives them one.

An admin cannot set their own password this way: their own row has no actions menu. An admin changes their own
password on the Account page, like anyone else.

### A person who joined through single sign-on

Someone whose account was created by the provider has no password. They can add one on their Account page by
giving only the new password, and then sign in with their email and that password too. An admin can also set
one for them, as above.

## Deleting

Open the actions menu on the person's row and choose "Delete", then confirm. The person can no longer sign in,
and their sessions are signed out within 5 minutes. They are gone from the Users page. The books they added
stay in the library, with no owner, and the audit log keeps what they did. It cannot be undone, and to let them
back in an admin adds them again, which makes a new account.

## What an admin cannot do

- Change their own role, delete their own account, or set their own password from the Users page. Another admin
  does the first two, and the Account page does the third.
- Leave Nook with no admin. Changing the only admin to another role, or deleting them, is refused.
- Change the role of a person who has a single sign-on login while single sign-on sets roles from groups, which
  is when `AdminGroup` or `ContributorGroup` is set. The row says "Set by single sign-on" and has no "Change
  role": the person's groups at the provider decide the role, and each sign-in sets it again.

Every one of these changes goes in the audit log: a user being added, a password being set by an admin, a role
being changed, a user being deleted, and a person changing their own password. The log never holds a password.

## When password sign-in is off

With `Nook:PasswordSignIn` set to `false`, the provider is the only way in, as
[Single sign-on only](single-sign-on.md#single-sign-on-only) describes. Nook then has no use for passwords, so
the Users page has no "Add user" and no "Set password", and the Account page has no password form. Changing a
role and deleting still work. New people come in by signing in at the provider.

## Keep a second way in

If the only admin forgets their password, nothing inside Nook can get them back in: an admin sets another
person's password, and there is no admin left to do it, and Nook sends no email. So keep a second way in before
you need one, by either of these:

- A second admin. Add someone you trust, or change a person you already have, to the admin role, and give them
  a password they will not lose. Either admin can then set the other's password. A password manager is the
  simplest way to keep one.
- The admin's account linked to single sign-on, with the provider's account kept safe. The first single
  sign-on with the same email links the two, when the provider sends `email_verified` as true, and from then
  on the provider is a way in that does not need the password. See
  [Existing accounts](single-sign-on.md#existing-accounts).

When groups set roles, a single sign-on sign-in can still take the admin role from the only admin: an admin who
signs in through the provider without being in `AdminGroup` becomes a contributor or viewer, and Nook's rule of
always keeping an admin does not stop it, because that rule covers the Users page only. Put the admin in
`AdminGroup` at the provider before they first sign in through it, as [Roles](single-sign-on.md#roles)
describes.
