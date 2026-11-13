import { Link, Route, Routes } from "react-router-dom";
import { StudentIdBar } from "./components/StudentIdBar";
import { ClassesPage } from "./pages/ClassesPage";
import { CreateClassPage } from "./pages/CreateClassPage";
import { HoldDetailPage } from "./pages/HoldDetailPage";
import { MyApplicationsPage } from "./pages/MyApplicationsPage";

export function App() {
  return (
    <div className="min-h-screen bg-slate-50 text-slate-900">
      <StudentIdBar />
      <Routes>
        <Route path="/" element={<ClassesPage />} />
        <Route path="/classes/new" element={<CreateClassPage />} />
        <Route path="/holds/:holdId" element={<HoldDetailPage />} />
        <Route path="/me" element={<MyApplicationsPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </div>
  );
}

function NotFoundPage() {
  return (
    <main className="mx-auto max-w-2xl px-4 py-6">
      <h1 className="text-2xl font-semibold">Page not found</h1>
      <p className="mt-2 text-sm text-slate-600">
        <Link to="/" className="underline">
          Go to the class list
        </Link>
        .
      </p>
    </main>
  );
}
