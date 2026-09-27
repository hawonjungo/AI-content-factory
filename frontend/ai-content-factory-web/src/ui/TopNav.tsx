import { Link, useLocation } from "react-router-dom";

/**
 * Thin top-level strip so the user can jump between the two independent
 * pipelines this app now has: single video (ContentProject) and multi-episode
 * Story/Series. Deliberately not a full app shell/layout - each page still
 * owns its own <main className="wz"> and everything else about its layout.
 */
export function TopNav() {
  const location = useLocation();
  const onStories = location.pathname.startsWith("/stories");

  return (
    <nav className="wz-topnav">
      <Link to="/" className={`wz-topnav-link${onStories ? "" : " wz-topnav-link-active"}`}>
        Video
      </Link>
      <Link to="/stories" className={`wz-topnav-link${onStories ? " wz-topnav-link-active" : ""}`}>
        Chuỗi truyện
      </Link>
    </nav>
  );
}

export default TopNav;
