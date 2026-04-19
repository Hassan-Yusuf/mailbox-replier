import { NavLink } from 'react-router-dom';

export function Sidebar() {
  return (
    <nav className="nav-rail" aria-label="Main navigation">
      <NavLink to="/review" title="Review Queue">Q</NavLink>
      <NavLink to="/skipped" title="Skip Audit">S</NavLink>
      <NavLink to="/runs" title="Run History">H</NavLink>
    </nav>
  );
}
