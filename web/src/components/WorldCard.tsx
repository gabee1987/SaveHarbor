import { formatAgo, formatClock, formatSize } from '../lib/format';
import { lockText } from '../lib/lockText';
import type { Version, World } from '../lib/snapshot';

export function LockLine({ world, now, userId }: { world: World; now: Date; userId: string }) {
  const lock = lockText(world.lock, now, userId, (date) => formatClock(date, now));
  return (
    <div className={`lock lock-${lock.state}`}>
      <span className="lock-text">{lock.text}</span>
      {lock.detail && <span className="muted small">{lock.detail}</span>}
    </div>
  );
}

export function versionLine(version: Version, now: Date): string {
  return `Version ${version.number} by ${version.uploaderName} · ${formatAgo(version.createdAt, now)} · ${formatSize(version.sizeBytes)}`;
}

export function WorldCard({ world, now, userId, href }: { world: World; now: Date; userId: string; href: string }) {
  return (
    <li className="card">
      <a className="card-title" href={href}>
        {world.name}
      </a>
      <LockLine world={world} now={now} userId={userId} />
      <p className="muted small">{world.latest ? versionLine(world.latest, now) : 'Nothing uploaded yet'}</p>
    </li>
  );
}
