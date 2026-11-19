# Turning on private vulnerability reporting

`SECURITY.md` asks people to report security problems through GitHub's private form, not a public issue. The form works only after the repository owner turns the feature on. The owner does this once, by hand, because it is a repository setting.

These steps follow GitHub's page "Configuring private vulnerability reporting for a repository", checked on 2026-11-20.

## Steps

1. Open the repository on GitHub: <https://github.com/TheAlsaadi/ClipBench>.
2. Click the **Settings** tab. If it is hidden, open the **More** (⋯) menu first, then **Settings**.
3. In the sidebar, under **Security and quality**, click **Advanced Security**.
4. Next to **Private vulnerability reporting**, click **Enable**.

## Check that it works

- Open the repository's **Security** tab, then **Advisories**. A **Report a vulnerability** button should be there.
- The address in `SECURITY.md` (`/security/advisories/new`) now opens the private form instead of a "not found" page.

## Get told when a report arrives

GitHub notifies the owner of a new report only when the owner watches the repository for it:

1. On the repository page, click **Watch**.
2. Choose **All Activity**, or **Custom** with **Security alerts** ticked.
3. For email as well, open your GitHub settings, then **Notifications**, and under **Subscriptions → Watching** tick **Email**.

## Afterwards

The response targets in `SECURITY.md` (acknowledge within 7 days, assess within 14) start from the moment the report arrives, so the notification matters as much as the switch.
